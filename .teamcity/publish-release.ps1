[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputDir,

    [Parameter(Mandatory = $true)]
    [string] $WorkDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputDir,

    [Parameter(Mandatory = $true)]
    [string] $AppVersion,

    [Parameter(Mandatory = $true)]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $TagName,

    [Parameter(Mandatory = $true)]
    [string] $ReleaseName,

    [Parameter(Mandatory = $true)]
    [string] $TargetCommitish,

    [switch] $SkipGitHubPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-NormalizedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $expandedPath = [Environment]::ExpandEnvironmentVariables($Path)
    if ([IO.Path]::IsPathRooted($expandedPath)) {
        return [IO.Path]::GetFullPath($expandedPath)
    }

    return [IO.Path]::GetFullPath((Join-Path -Path (Get-Location).ProviderPath -ChildPath $expandedPath))
}

function Test-PathInside {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $ParentPath
    )

    $trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidatePath = [IO.Path]::GetFullPath($Path).TrimEnd($trimChars)
    $containerPath = [IO.Path]::GetFullPath($ParentPath).TrimEnd($trimChars)

    return $candidatePath.Equals($containerPath, [StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($containerPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($containerPath + [IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-ReleasePathAllowed {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $checkoutPath = [IO.Path]::GetFullPath((Get-Location).ProviderPath).TrimEnd($trimChars)
    $candidatePath = [IO.Path]::GetFullPath($Path).TrimEnd($trimChars)
    $sourcesPath = [IO.Path]::GetFullPath((Join-Path -Path $checkoutPath -ChildPath "Sources")).TrimEnd($trimChars)
    $outPath = [IO.Path]::GetFullPath((Join-Path -Path $checkoutPath -ChildPath "out")).TrimEnd($trimChars)

    if ($candidatePath.Equals($checkoutPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must not point at the checkout root. Current value: $candidatePath"
    }

    if (Test-PathInside -Path $candidatePath -ParentPath $sourcesPath) {
        throw "$Name must not point inside Sources. Current value: $candidatePath"
    }

    if (Test-PathInside -Path $candidatePath -ParentPath $checkoutPath) {
        if (-not (Test-PathInside -Path $candidatePath -ParentPath $outPath) -or
            $candidatePath.Equals($outPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Name must point under out/ when it is inside the checkout. Current value: $candidatePath"
        }
    }
}

function Resolve-InputZip {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string] $Version
    )

    $expectedZip = Join-Path -Path $SourceDirectory -ChildPath "YoloEase.$Version.zip"
    if (Test-Path -LiteralPath $expectedZip -PathType Leaf) {
        return $expectedZip
    }

    $candidateZips = @(Get-ChildItem -LiteralPath $SourceDirectory -Filter "*.zip" -File)
    if ($candidateZips.Count -eq 1) {
        return $candidateZips[0].FullName
    }

    $found = ($candidateZips | ForEach-Object { $_.Name }) -join ", "
    throw "Expected YoloEase.$Version.zip or a single zip in $SourceDirectory. Found: $found"
}

function Expand-ZipToDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ZipPath,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -Path $Destination -ItemType Directory -Force | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination -Force
}

function New-ZipFromDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string] $ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    [IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDirectory,
        $ZipPath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Ensure-ZipDirectoryEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ZipPath,

        [Parameter(Mandatory = $true)]
        [string] $EntryName
    )

    $zip = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        if ($null -eq $zip.GetEntry($EntryName)) {
            [void]$zip.CreateEntry($EntryName)
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [switch] $AllowFailure
    )

    $output = & git @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode. $output"
    }

    if ($exitCode -ne 0) {
        return $null
    }

    return $output
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Method,

        [Parameter(Mandatory = $true)]
        [string] $Uri,

        [object] $Body = $null,

        [switch] $AllowNotFound
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $script:GitHubHeaders
    }

    if ($null -ne $Body) {
        $parameters["Body"] = $Body | ConvertTo-Json -Depth 10
        $parameters["ContentType"] = "application/json"
    }

    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        $response = $_.Exception.Response
        $statusCode = $null
        if ($null -ne $response) {
            try {
                $statusCode = [int]$response.StatusCode
            }
            catch {
                $statusCode = $null
            }
        }

        if ($AllowNotFound -and $statusCode -eq 404) {
            return $null
        }

        $message = $_.Exception.Message
        try {
            if ($null -ne $response) {
                $stream = $response.GetResponseStream()
                if ($null -ne $stream) {
                    $reader = [IO.StreamReader]::new($stream)
                    $responseBody = $reader.ReadToEnd()
                    if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
                        $message = "$message $responseBody"
                    }
                }
            }
        }
        catch {
            # Keep the original API error if the response body cannot be read.
        }

        throw "GitHub API $Method $Uri failed: $message"
    }
}

function Get-ReleaseNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ApiBase
    )

    $publishedReleases = @(Invoke-GitHubJson -Method "Get" -Uri "$ApiBase/releases?per_page=100" -AllowNotFound)
    $latestRelease = $publishedReleases |
        Where-Object { -not $_.draft -and -not [string]::IsNullOrWhiteSpace($_.published_at) } |
        Sort-Object -Property published_at -Descending |
        Select-Object -First 1
    $lastTag = $null

    if ($null -ne $latestRelease -and -not [string]::IsNullOrWhiteSpace($latestRelease.tag_name)) {
        $lastTag = [string]$latestRelease.tag_name
    }

    Invoke-Git -Arguments @("fetch", "--tags", "--force", "--quiet", "origin") -AllowFailure | Out-Null

    $formatArgument = "--pretty=format:- %s (%h)"
    if (-not [string]::IsNullOrWhiteSpace($lastTag)) {
        $tagCommit = Invoke-Git -Arguments @("rev-parse", "--verify", "$lastTag^{commit}") -AllowFailure

        if ($null -ne $tagCommit) {
            $changes = Invoke-Git -Arguments @("log", $formatArgument, "$lastTag..HEAD") -AllowFailure
            $changesText = ($changes | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"

            if ([string]::IsNullOrWhiteSpace($changesText)) {
                $changesText = "- No git changes found since $lastTag."
            }

            return "## Changes since $lastTag`n`n$changesText"
        }
    }

    $recentChanges = Invoke-Git -Arguments @("log", $formatArgument, "-n", "50") -AllowFailure
    $recentChangesText = ($recentChanges | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"

    if ([string]::IsNullOrWhiteSpace($recentChangesText)) {
        $recentChangesText = "- No git changes found."
    }

    return "## Recent changes`n`n$recentChangesText"
}

function Upload-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Release,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $uploadBase = [string]$Release.upload_url -replace "\{\?name,label\}$", ""
    $fileName = [IO.Path]::GetFileName($Path)
    $uploadUri = "{0}?name={1}&label={2}" -f $uploadBase, [Uri]::EscapeDataString($fileName), [Uri]::EscapeDataString($Label)

    try {
        Invoke-RestMethod `
            -Method Post `
            -Uri $uploadUri `
            -Headers $script:GitHubHeaders `
            -ContentType "application/zip" `
            -InFile $Path | Out-Null
    }
    catch {
        throw "Failed to upload release asset $fileName. $($_.Exception.Message)"
    }
}

$InputDir = Get-NormalizedPath -Path $InputDir
$WorkDir = Get-NormalizedPath -Path $WorkDir
$OutputDir = Get-NormalizedPath -Path $OutputDir

Assert-ReleasePathAllowed -Path $InputDir -Name "InputDir"
Assert-ReleasePathAllowed -Path $WorkDir -Name "WorkDir"
Assert-ReleasePathAllowed -Path $OutputDir -Name "OutputDir"

if (-not (Test-Path -LiteralPath $InputDir -PathType Container)) {
    throw "Input directory does not exist: $InputDir"
}

foreach ($directory in @($WorkDir, $OutputDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -Path $directory -ItemType Directory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($TargetCommitish) -or $TargetCommitish.StartsWith("%")) {
    $TargetCommitish = (Invoke-Git -Arguments @("rev-parse", "HEAD")).Trim()
}

$repositoryParts = $Repository.Split("/")
if ($repositoryParts.Count -ne 2) {
    throw "Repository must use owner/name format. Current value: $Repository"
}

$owner = [Uri]::EscapeDataString($repositoryParts[0])
$repo = [Uri]::EscapeDataString($repositoryParts[1])
$apiBase = "https://api.github.com/repos/$owner/$repo"

$sourceZip = Resolve-InputZip -SourceDirectory $InputDir -Version $AppVersion
$portableDirectory = Join-Path -Path $WorkDir -ChildPath "portable"
$nonPortableDirectory = Join-Path -Path $WorkDir -ChildPath "non-portable"
$portableZip = Join-Path -Path $OutputDir -ChildPath "YoloEase.$AppVersion.portable.zip"
$nonPortableZip = Join-Path -Path $OutputDir -ChildPath "YoloEase.$AppVersion.zip"

Write-Host "Preparing release packages from $sourceZip"

Add-Type -AssemblyName System.IO.Compression.FileSystem

Expand-ZipToDirectory -ZipPath $sourceZip -Destination $portableDirectory
Expand-ZipToDirectory -ZipPath $sourceZip -Destination $nonPortableDirectory

$portableDataDirectory = Join-Path -Path $portableDirectory -ChildPath "data"
if (-not (Test-Path -LiteralPath $portableDataDirectory -PathType Container)) {
    New-Item -Path $portableDataDirectory -ItemType Directory -Force | Out-Null
}

$nonPortableDataDirectory = Join-Path -Path $nonPortableDirectory -ChildPath "data"
if (Test-Path -LiteralPath $nonPortableDataDirectory) {
    Remove-Item -LiteralPath $nonPortableDataDirectory -Recurse -Force
}

New-ZipFromDirectory -SourceDirectory $portableDirectory -ZipPath $portableZip
Ensure-ZipDirectoryEntry -ZipPath $portableZip -EntryName "data/"
New-ZipFromDirectory -SourceDirectory $nonPortableDirectory -ZipPath $nonPortableZip

if ($SkipGitHubPublish) {
    Write-Host "Skipping GitHub release publishing."
    exit 0
}

$githubToken = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($githubToken)) {
    throw "Set a TeamCity secure environment parameter named env.GITHUB_TOKEN with GitHub Contents: write permission for $Repository."
}

$script:GitHubHeaders = @{
    Accept = "application/vnd.github+json"
    Authorization = "Bearer $githubToken"
    "X-GitHub-Api-Version" = "2026-03-10"
    "User-Agent" = "YoloEase-TeamCity-Publish"
}

$encodedTagName = [Uri]::EscapeDataString($TagName)
$existingRelease = Invoke-GitHubJson -Method "Get" -Uri "$apiBase/releases/tags/$encodedTagName" -AllowNotFound
if ($null -ne $existingRelease) {
    throw "GitHub release $TagName already exists at $($existingRelease.html_url). Use a new AppVersion or remove the existing release before retrying."
}

$releaseNotes = Get-ReleaseNotes -ApiBase $apiBase
$release = Invoke-GitHubJson -Method "Post" -Uri "$apiBase/releases" -Body @{
    tag_name = $TagName
    target_commitish = $TargetCommitish
    name = $ReleaseName
    body = $releaseNotes
    draft = $true
    prerelease = $false
    generate_release_notes = $false
}

Upload-ReleaseAsset -Release $release -Path $portableZip -Label "Portable"
Upload-ReleaseAsset -Release $release -Path $nonPortableZip -Label "Non-portable"

Write-Host "Created draft release $ReleaseName at $($release.html_url)"
