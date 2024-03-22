using System.IO.Compression;
using System.Linq;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Threading;
using CliWrap;
using CliWrap.Builders;
using CliWrap.EventStream;
using PoeShared.Logging;

namespace YoloEase.UI.Cvat;

public partial class CvatCliWrapper
{
    private static readonly IFluentLog Log = typeof(CvatCliWrapper).PrepareLogger();

    private const string CvatAppName = "cvat-cli";
    private readonly FileInfo wrapperScript;

    public CvatCliWrapper()
    {
        var wrapperScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "CVATWrapper.py");
        wrapperScript = new FileInfo(wrapperScriptPath);
        if (!wrapperScript.Exists)
        {
            throw new FileNotFoundException(message: $"CVAT Wrapper script not found @ {wrapperScript.FullName}");
        }
    }

    public string Username { get; set; }

    public string Password { get; set; }

    public Uri BaseAddress { get; set; }

    public async Task EnsureInstalled()
    {
    }

    public async Task DownloadAnnotations(
        int taskId,
        FileInfo outputFile)
    {
        var zipOutputFileName = $"{Path.GetFileNameWithoutExtension(outputFile.FullName)}.zip";
        var zipOutputFile = new FileInfo(Path.Combine(outputFile.DirectoryName, zipOutputFileName));

        if (zipOutputFile.Exists)
        {
            zipOutputFile.Delete();
        }
        using var zipRemove = Disposable.Create(() =>
        {
            zipOutputFile.Refresh();
            if (zipOutputFile.Exists)
            {
                zipOutputFile.Delete();
            }
        });
        
        if (outputFile.Exists)
        {
            outputFile.Delete();
        }

        var format = "CVAT for images 1.1";
        
        var cmd = Cli.Wrap(CvatAppName)
            .WithWorkingDirectory(AppDomain.CurrentDomain.BaseDirectory)
            .WithArguments(x =>
            {
                EnrichCvatCliArguments(x);
                x.Add($"dump", escape: false);
                x.Add($"--format  \"{format}\"", escape: false);
                x.Add($"{taskId}", escape: false);
                x.Add($"\"{zipOutputFile.FullName}\"", escape: false);
            });

        var successParser = DumpDatasetHasBeenDownloaded();

        bool? success = null;
        await foreach (var cmdEvent in cmd.ListenAndLogAsync())
        {
            switch (cmdEvent)
            {
                case StandardOutputCommandEvent stdOut:
                    if (successParser.IsMatch(stdOut.Text))
                    {
                        success = true;
                    }
                    break;
            }
        }

        if (success == null)
        {
            throw new InvalidOperationException("Failed to download annotations");
        }
        
        zipOutputFile.Refresh();
        if (!zipOutputFile.Exists)
        {
            throw new InvalidOperationException($"Failed to download annotations to {zipOutputFile.FullName}");
        }

        await using var zipStream = new FileStream(zipOutputFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zipArchive = new ZipArchive(zipStream);

        if (zipArchive.Entries.Count != 1)
        {
            throw new InvalidOperationException($"It is expected that annotations archive will contain a single file, instead got: {zipArchive.Entries.Select(x => x.Name).DumpToString()}");
        }

        var annotationsEntry = zipArchive.Entries.Single();
        
        annotationsEntry.ExtractToFile(outputFile.FullName);

        outputFile.Refresh();
        if (!outputFile.Exists)
        {
            throw new InvalidOperationException($"Failed to dump annotations to {outputFile.FullName}");
        }
    }

    public async Task<int> CreateTask(
        string organization,
        int projectId,
        string taskName,
        IReadOnlyList<FileInfo> filesToUpload)
    {
        if (!File.Exists(wrapperScript.FullName))
        {
            throw new FileNotFoundException($"CVAT Wrapper script not found @ {wrapperScript.FullName}", wrapperScript.Name);
        }
        
        using var tmpScriptFile = new TempFile(wrapperScript);

        TempFile tmpFileListFile = null;
        if (filesToUpload.Count > 10)
        {
            tmpFileListFile = new TempFile();
            await File.WriteAllLinesAsync(tmpFileListFile.File.FullName, filesToUpload.Select(x => x.FullName).ToArray(), CancellationToken.None);
        }
        using var tmpFileListFileAnchors = tmpFileListFile;
        
        var cmd = Cli.Wrap("python")
            .WithWorkingDirectory(AppDomain.CurrentDomain.BaseDirectory)
            .WithArguments(x =>
            {
                x.Add($"\"{tmpScriptFile.File.FullName}\"", escape: false);

                x.Add($"task.create", escape: false);
                x.Add($"--task-name \"{taskName}\"", escape: false);
                EnrichWrapperArguments(x);

                x.Add($"--organization \"{organization}\"", escape: false);
                x.Add($"--project-id {projectId}", escape: false);

                if (tmpFileListFile == null)
                {
                    var filesPaths = string.Join(" ", filesToUpload.Select(x => $"\"{x.FullName}\""));
                    x.Add($"--file-paths {filesPaths}" , escape: false);
                }
                else
                {
                    x.Add($"--file-list \"{tmpFileListFile.File.FullName}\"", escape: false);
                }
            });

        var taskIdParser = CreateTaskParseTaskId();
        
        int? taskId = null;
        await foreach (var cmdEvent in cmd.ListenAndLogAsync())
        {
            var text = string.Empty;
            switch (cmdEvent)
            {
                case StandardOutputCommandEvent stdOut:
                    text = stdOut.Text;
                    break;

                case StandardErrorCommandEvent stdErr:
                    text = stdErr.Text;
                    break;
            }
            
            var taskIdMatch = taskIdParser.Match(text);
            if (taskIdMatch.Success)
            {
                taskId = int.Parse(taskIdMatch.Groups["taskId"].Value);
            }
        }

        if (taskId == null)
        {
            throw new InvalidOperationException("Failed to get task ID as a result of running Create");
        }

        return taskId.Value;
    }

    public async Task<int> CreateTaskViaCvatCli(
        string organization,
        int projectId,
        string taskName,
        IEnumerable<FileInfo> filesToUpload)
    {
        var cmd = Cli.Wrap(CvatAppName)
            .WithWorkingDirectory(AppDomain.CurrentDomain.BaseDirectory)
            .WithArguments(x =>
            {
                EnrichCvatCliArguments(x);

                x.Add($"--organization \"{organization}\"", escape: false);

                x.Add($"create \"{taskName}\"", escape: false);
                x.Add($"--project_id {projectId}", escape: false);

                x.Add($"local", escape: false);
                x.Add(string.Join(" ", filesToUpload.Select(x => $"\"{x.FullName}\"")), escape: false);
            });

        var taskIdParser = CreateTaskParseTaskId();
        
        int? taskId = null;
        await foreach (var cmdEvent in cmd.ListenAndLogAsync())
        {
            var text = string.Empty;
            switch (cmdEvent)
            {
                case StandardOutputCommandEvent stdOut:
                    text = stdOut.Text;
                    break;

                case StandardErrorCommandEvent stdErr:
                    text = stdErr.Text;
                    break;
            }
            
            var taskIdMatch = taskIdParser.Match(text);
            if (taskIdMatch.Success)
            {
                taskId = int.Parse(taskIdMatch.Groups["taskId"].Value);
            }
        }

        if (taskId == null)
        {
            throw new InvalidOperationException("Failed to get task ID as a result of running Create");
        }

        return taskId.Value;
    }

    [GeneratedRegex("Created task ID\\:\\s*(?'taskId'\\d+)\\s*", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex CreateTaskParseTaskId();
    
    [GeneratedRegex("Dataset for task .* has been downloaded", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex DumpDatasetHasBeenDownloaded();


    private void EnrichWrapperArguments(ArgumentsBuilder x)
    {
        x.Add($"--host \"{BaseAddress.Host}\"", escape: false);
        if (BaseAddress.Port != 0 && !BaseAddress.IsDefaultPort)
        {
            x.Add($"--port \"{BaseAddress.Port}\"", escape: false);
        }

        x.Add($"--username \"{Username}\"", escape: false);
        x.Add($"--password \"{Password}\"", escape: false);
    }
    
    private void EnrichCvatCliArguments(ArgumentsBuilder x)
    {
        if (BaseAddress.Scheme.ToLower() == "http")
        {
            x.Add("--insecure", escape: false);
        }

        x.Add($"--server-host \"{BaseAddress.Host}\"", escape: false);
        if (BaseAddress.Port != 0 && !BaseAddress.IsDefaultPort)
        {
            x.Add($"--server-port \"{BaseAddress.Port}\"", escape: false);
        }

        x.Add($"--auth \"{Username}:{Password}\"", escape: false);
        x.Add($"--debug");
    }
}