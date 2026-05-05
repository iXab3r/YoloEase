using System.Text.RegularExpressions;
using System.Threading;
using CliWrap;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Implements detection and remediation actions for the managed prerequisite toolchain.
/// </summary>
public sealed partial class PrerequisitesInstaller : DisposableReactiveObjectWithLogger
{
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan PytorchInstallTimeout = TimeSpan.FromMinutes(45);

    private readonly IPrerequisitesToolchain toolchain;
    private readonly IPrerequisiteCommandRunner commandRunner;
    private readonly IGpuRuntimeDetector gpuRuntimeDetector;

    public PrerequisitesInstaller(
        IPrerequisitesToolchain toolchain,
        IPrerequisiteCommandRunner commandRunner,
        IGpuRuntimeDetector gpuRuntimeDetector)
    {
        this.toolchain = toolchain;
        this.commandRunner = commandRunner;
        this.gpuRuntimeDetector = gpuRuntimeDetector;
    }

    public async Task<bool?> CheckPythonAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput($"Checking managed Python at {toolchain.PythonExecutable.FullName}");
        if (!toolchain.PythonExecutable.Exists)
        {
            check.AppendOutput($"Expected at {toolchain.PythonExecutable.FullName}");
            return false;
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.PythonExecutable.FullName).WithArguments("--version"),
            "python-version",
            QuickTimeout,
            cancellationToken,
            check.AppendOutput);
        return result.IsSuccess && result.CombinedOutput.Contains("Python 3.11", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InstallPythonAsync(CheckItem check, CancellationToken cancellationToken)
    {
        toolchain.EnsureBaseDirectories();
        toolchain.EnsureManagedPath(toolchain.PythonDirectory);
        check.AppendOutput($"Installing managed Python into {toolchain.PythonDirectory.FullName}");

        if (toolchain.PythonDirectory.Exists && !toolchain.PythonExecutable.Exists)
        {
            check.AppendOutput($"Removing incomplete Python directory {toolchain.PythonDirectory.FullName}");
            toolchain.PythonDirectory.Delete(recursive: true);
        }

        var installer = await DownloadPythonInstallerAsync(check, cancellationToken);
        var result = await commandRunner.RunAsync(
            Cli.Wrap(installer.FullName).WithArguments(x =>
            {
                x.Add("/quiet");
                x.Add("InstallAllUsers=0");
                x.Add($"TargetDir={toolchain.PythonDirectory.FullName}");
                x.Add("Include_launcher=0");
                x.Add("Include_pip=1");
                x.Add("Include_test=0");
                x.Add("Include_doc=0");
                x.Add("Include_tcltk=0");
                x.Add("PrependPath=0");
                x.Add("Shortcuts=0");
            }),
            "python-install",
            InstallTimeout,
            cancellationToken,
            check.AppendOutput);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Python installer failed with exit code {result.ExitCode}: {TrimForError(result.CombinedOutput)}");
        }
        check.AppendOutput($"Managed Python installed at {toolchain.PythonExecutable.FullName}");
    }

    public async Task<bool?> CheckVenvAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput($"Checking managed Python environment at {toolchain.VenvPythonExecutable.FullName}");
        if (!toolchain.VenvPythonExecutable.Exists)
        {
            check.AppendOutput($"Expected at {toolchain.VenvPythonExecutable.FullName}");
            return false;
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.VenvPythonExecutable.FullName).WithArguments("--version"),
            "venv-python-version",
            QuickTimeout,
            cancellationToken,
            check.AppendOutput);
        return result.IsSuccess && result.CombinedOutput.Contains("Python 3.11", StringComparison.OrdinalIgnoreCase);
    }

    public async Task CreateVenvAsync(CheckItem check, CancellationToken cancellationToken)
    {
        var python = toolchain.RequirePythonExecutable();
        toolchain.EnsureManagedPath(toolchain.VenvDirectory);
        check.AppendOutput($"Creating managed Python environment at {toolchain.VenvDirectory.FullName}");
        if (toolchain.VenvDirectory.Exists)
        {
            check.AppendOutput($"Removing previous environment at {toolchain.VenvDirectory.FullName}");
            toolchain.VenvDirectory.Delete(recursive: true);
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(python.FullName).WithArguments(x =>
            {
                x.Add("-m");
                x.Add("venv");
                x.Add(toolchain.VenvDirectory.FullName);
            }),
            "venv-create",
            InstallTimeout,
            cancellationToken,
            check.AppendOutput);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to create Python environment: {TrimForError(result.CombinedOutput)}");
        }
        check.AppendOutput($"Managed Python environment created at {toolchain.VenvDirectory.FullName}");
    }

    public async Task<bool?> CheckPipAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput($"Checking pip inside managed environment at {toolchain.VenvPipExecutable.FullName}");
        if (!toolchain.VenvPipExecutable.Exists)
        {
            check.AppendOutput($"Expected at {toolchain.VenvPipExecutable.FullName}");
            return false;
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.VenvPythonExecutable.FullName).WithArguments(x =>
            {
                x.Add("-m");
                x.Add("pip");
                x.Add("--version");
            }),
            "pip-version",
            QuickTimeout,
            cancellationToken,
            check.AppendOutput);
        return result.IsSuccess;
    }

    public async Task UpgradePipAsync(CheckItem check, CancellationToken cancellationToken)
    {
        var python = toolchain.RequireVenvPythonExecutable();
        check.AppendOutput("Upgrading pip, wheel, and setuptools in the managed environment");
        var result = await commandRunner.RunAsync(
            Cli.Wrap(python.FullName).WithArguments(x =>
            {
                x.Add("-m");
                x.Add("pip");
                x.Add("install");
                x.Add("--upgrade");
                x.Add("pip");
                x.Add("wheel");
                x.Add("setuptools");
            }),
            "pip-upgrade",
            InstallTimeout,
            cancellationToken,
            check.AppendOutput);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to upgrade pip: {TrimForError(result.CombinedOutput)}");
        }
        check.AppendOutput("pip, wheel, and setuptools are ready");
    }

    public async Task<bool?> CheckGpuDriverHelpAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput("Checking GPU driver state");
        var runtimeInfo = await gpuRuntimeDetector.DetectAsync(check.AppendOutput, cancellationToken);
        AppendGpuRuntimeSummary(check, runtimeInfo);
        check.AppendOutput(gpuRuntimeDetector.BuildDriverGuidance(runtimeInfo));

        if (runtimeInfo.PrimaryNvidiaGpu != null)
        {
            return runtimeInfo.HasCompatibleNvidiaDriver;
        }

        return false;
    }

    public async Task OpenGpuDriverHelpAsync(CheckItem check, CancellationToken cancellationToken)
    {
        var runtimeInfo = await gpuRuntimeDetector.DetectAsync(check.AppendOutput, cancellationToken);
        var guidance = gpuRuntimeDetector.BuildDriverGuidance(runtimeInfo);
        check.AppendOutput(guidance);
        var helpUri = gpuRuntimeDetector.GetDriverHelpUri(runtimeInfo);
        if (string.IsNullOrWhiteSpace(helpUri))
        {
            check.AppendOutput("No vendor-specific driver page is available because no supported GPU vendor was detected.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        check.AppendOutput($"Opening driver help: {helpUri}");
        await ProcessUtils.OpenUri(helpUri);
    }

    public async Task<bool?> CheckPytorchRuntimeAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput("Checking managed PyTorch runtime");
        var runtimeInfo = await gpuRuntimeDetector.DetectAsync(check.AppendOutput, cancellationToken);
        AppendGpuRuntimeSummary(check, runtimeInfo);

        if (!runtimeInfo.PyTorch.IsInstalled)
        {
            check.AppendOutput("PyTorch is not installed in the managed environment.");
            return false;
        }

        if (runtimeInfo.ShouldInstallCudaPyTorch)
        {
            if (!runtimeInfo.PyTorch.IsCudaBuild)
            {
                check.AppendOutput("Compatible NVIDIA GPU detected, but managed PyTorch is CPU-only.");
                return false;
            }

            if (!runtimeInfo.PyTorch.CudaAvailable)
            {
                check.AppendOutput("Compatible NVIDIA GPU detected, but PyTorch CUDA is not available.");
                return false;
            }
        }

        check.AppendOutput(runtimeInfo.ShouldInstallCudaPyTorch
            ? "Managed CUDA PyTorch runtime is ready."
            : "Managed CPU PyTorch runtime is ready.");
        return true;
    }

    public async Task InstallPytorchRuntimeAsync(CheckItem check, CancellationToken cancellationToken)
    {
        var python = toolchain.RequireVenvPythonExecutable();
        var runtimeInfo = await gpuRuntimeDetector.DetectAsync(check.AppendOutput, cancellationToken);
        var useCuda = runtimeInfo.ShouldInstallCudaPyTorch;
        var packageIndex = useCuda ? GpuRuntimeDetector.CudaPyTorchIndexUrl : GpuRuntimeDetector.CpuPyTorchIndexUrl;
        var runtimeFlavor = useCuda ? "CUDA 13.0" : "CPU";
        check.AppendOutput($"Installing {runtimeFlavor} PyTorch runtime into the managed environment");
        check.AppendOutput($"Package index: {packageIndex}");
        check.AppendOutput($"Packages: torch=={GpuRuntimeDetector.TorchVersion}, torchvision=={GpuRuntimeDetector.TorchVisionVersion}");

        var result = await commandRunner.RunAsync(
            Cli.Wrap(python.FullName).WithArguments(x =>
            {
                x.Add("-m");
                x.Add("pip");
                x.Add("install");
                x.Add("--upgrade");
                x.Add("--index-url");
                x.Add(packageIndex);
                x.Add($"torch=={GpuRuntimeDetector.TorchVersion}");
                x.Add($"torchvision=={GpuRuntimeDetector.TorchVisionVersion}");
            }),
            "pytorch-runtime-install",
            PytorchInstallTimeout,
            cancellationToken,
            check.AppendOutput);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to install {runtimeFlavor} PyTorch runtime: {TrimForError(result.CombinedOutput)}");
        }

        check.AppendOutput($"{runtimeFlavor} PyTorch runtime is installed.");
    }

    public async Task<bool?> CheckPackagesAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput("Checking required Python packages: cv2, numpy, matplotlib, shapely, onnx, onnxruntime, ultralytics, torch");
        if (!toolchain.VenvPythonExecutable.Exists)
        {
            check.AppendOutput($"Expected at {toolchain.VenvPythonExecutable.FullName}");
            return false;
        }

        const string code = "import cv2, numpy, matplotlib, shapely, onnx, onnxruntime, ultralytics, torch; print('packages ok'); print('torch', torch.__version__); print('onnx', onnx.__version__); print('onnxruntime', onnxruntime.__version__)";
        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.VenvPythonExecutable.FullName).WithArguments(x =>
            {
                x.Add("-c");
                x.Add(code);
            }),
            "python-packages-check",
            QuickTimeout,
            cancellationToken,
            check.AppendOutput);
        return result.IsSuccess;
    }

    public async Task InstallPackagesAsync(CheckItem check, CancellationToken cancellationToken)
    {
        var python = toolchain.RequireVenvPythonExecutable();
        toolchain.RequirementsFile.Refresh();
        check.AppendOutput($"Installing packages from {toolchain.RequirementsFile.FullName}");
        if (!toolchain.RequirementsFile.Exists)
        {
            throw new FileNotFoundException($"Requirements file not found: {toolchain.RequirementsFile.FullName}");
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(python.FullName).WithArguments(x =>
            {
                x.Add("-m");
                x.Add("pip");
                x.Add("install");
                x.Add("-r");
                x.Add(toolchain.RequirementsFile.FullName);
            }),
            "python-packages-install",
            InstallTimeout,
            cancellationToken,
            check.AppendOutput);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to install Python packages: {TrimForError(result.CombinedOutput)}");
        }
        check.AppendOutput("Required Python packages are installed");
    }

    public async Task<bool?> CheckYoloAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput($"Checking YOLO CLI at {toolchain.YoloExecutable.FullName}");
        if (!toolchain.YoloExecutable.Exists)
        {
            check.AppendOutput($"Expected at {toolchain.YoloExecutable.FullName}");
            return false;
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.YoloExecutable.FullName).WithArguments("checks"),
            "yolo-checks",
            TimeSpan.FromMinutes(2),
            cancellationToken,
            check.AppendOutput);
        return result.IsSuccess && TryParseYoloChecks(result.CombinedOutput, out _);
    }

    public async Task<bool?> CheckCvatCliAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput($"Checking CVAT CLI at {toolchain.CvatCliExecutable.FullName}");
        if (!toolchain.CvatCliExecutable.Exists)
        {
            check.AppendOutput($"Expected at {toolchain.CvatCliExecutable.FullName}");
            return false;
        }

        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.CvatCliExecutable.FullName).WithArguments("--version"),
            "cvat-cli-version",
            QuickTimeout,
            cancellationToken,
            check.AppendOutput);
        return result.IsSuccess;
    }

    public async Task<bool?> CheckGpuAsync(CheckItem check, CancellationToken cancellationToken)
    {
        check.AppendOutput("Checking CUDA availability through PyTorch");
        var pytorch = await gpuRuntimeDetector.ProbePyTorchAsync(check.AppendOutput, cancellationToken);
        check.AppendOutput(pytorch.Summary);
        return pytorch.IsInstalled && pytorch.CudaAvailable;
    }

    public static bool TryParseYoloChecks(string text, out Yolo8ChecksResult? checksResult)
    {
        checksResult = null;
        var match = YoloChecksParserRegex().Match(text ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        checksResult = new Yolo8ChecksResult
        {
            YoloVersion = match.Groups["YoloVersion"].Value,
            PythonVersion = match.Groups["PythonVersion"].Value,
            TorchVersion = match.Groups["TorchVersion"].Value,
            DeviceIndex = match.Groups["DeviceIndex"].Value,
            DeviceType = match.Groups["DeviceType"].Value,
            DeviceName = match.Groups["DeviceName"].Value,
        };
        return true;
    }

    private async Task<FileInfo> DownloadPythonInstallerAsync(CheckItem check, CancellationToken cancellationToken)
    {
        toolchain.EnsureBaseDirectories();
        var targetFile = new FileInfo(Path.Combine(toolchain.DownloadsDirectory.FullName, Path.GetFileName(toolchain.PythonInstallerUri.LocalPath)));
        targetFile.Refresh();
        if (targetFile.Exists)
        {
            check.AppendOutput($"Using cached Python installer: {targetFile.FullName}");
            return targetFile;
        }

        var tempFile = new FileInfo(targetFile.FullName + ".tmp");
        if (tempFile.Exists)
        {
            check.AppendOutput($"Removing stale Python installer download: {tempFile.FullName}");
            tempFile.Delete();
        }

        check.AppendOutput($"Downloading Python installer from {toolchain.PythonInstallerUri}");
        check.AppendOutput($"Download target: {targetFile.FullName}");
        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(toolchain.PythonInstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        check.AppendOutput(totalBytes is > 0
            ? $"Python installer size: {FormatBytes(totalBytes.Value)}"
            : "Python installer size is unknown");
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(tempFile.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1024 * 128];
            var downloadedBytes = 0L;
            var lastProgressReport = DateTimeOffset.MinValue;
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;
                if (DateTimeOffset.Now - lastProgressReport < TimeSpan.FromSeconds(1) && downloadedBytes != totalBytes)
                {
                    continue;
                }

                lastProgressReport = DateTimeOffset.Now;
                check.AppendOutput(totalBytes is > 0
                    ? $"Downloading Python installer: {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes.Value)}"
                    : $"Downloading Python installer: {FormatBytes(downloadedBytes)}");
            }
        }

        if (targetFile.Exists)
        {
            targetFile.Delete();
        }
        tempFile.MoveTo(targetFile.FullName);
        check.AppendOutput($"Python installer saved to {targetFile.FullName}");
        return targetFile;
    }

    private static string FormatBytes(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        return $"{bytes / megabyte:F1} MB";
    }

    private static string TrimForError(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "no output";
        }

        return value.Length <= 1200 ? value : value[^1200..];
    }

    private static void AppendGpuRuntimeSummary(CheckItem check, GpuRuntimeInfo runtimeInfo)
    {
        if (runtimeInfo.Adapters.Count <= 0)
        {
            check.AppendOutput("No NVIDIA or AMD GPU adapter was detected.");
        }
        else
        {
            foreach (var adapter in runtimeInfo.Adapters)
            {
                var driver = string.IsNullOrWhiteSpace(adapter.DriverVersion)
                    ? "driver unknown"
                    : $"driver {adapter.DriverVersion}";
                check.AppendOutput($"{adapter.Vendor}: {adapter.Name} ({driver}, {adapter.Source})");
            }
        }

        check.AppendOutput(runtimeInfo.PyTorch.Summary);
    }

    [GeneratedRegex(@"Ultralytics\s+(?'YoloVersion'.*?)\s+(?'PythonVersion'.*?)\s+(?'TorchVersion'.*?)\+(?'DeviceType'.*?)\s+(?'DeviceIndex'[\w\:]+)(?:\s+\((?'DeviceName'.*)\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex YoloChecksParserRegex();
}
