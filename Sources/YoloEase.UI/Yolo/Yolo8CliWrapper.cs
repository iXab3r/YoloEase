using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using CliWrap;
using CliWrap.Builders;
using CliWrap.EventStream;
using YoloEase.UI.Prerequisites;

namespace YoloEase.UI.Yolo;

/// <summary>
/// Describes the CVAT annotation files and output options used by the YOLO conversion script.
/// </summary>
public sealed record Yolo8ConvertAnnotationsArguments
{
    public DirectoryInfo OutputDirectory { get; init; }
    public FileInfo[] Annotations { get; init; }
    public bool UseSymlinks { get; init; }
    public int TrainValPercentage { get; init; } = 80;
}

/// <summary>
/// Runs managed Ultralytics YOLO commands and parses progress, health-check, and export output.
/// </summary>
public sealed partial class Yolo8CliWrapper : DisposableReactiveObjectWithLogger
{
    private const string DefaultTrainingWorkersArgument = "workers=0";

    [GeneratedRegex("\\s*(?'EpochCurrent'\\d+)\\/(?'EpochMax'\\d+)\\s*(?'VideoRAM'[\\w\\.]+).*?(?'EpochProgressPercentage'\\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TrainProgressParserRegex();

    [GeneratedRegex(@"image (?'ImageCurrent'\d+)/(?'ImageMax'\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PredictProgressParserRegex();
    
    [GeneratedRegex(@"Scanning\s+(?'path'.*)\.\.\..*?(?=(?'ImageCurrent'\d+)[\/\\](?'ImageMax'\d+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TrainScanningProgressParserRegex();

    [GeneratedRegex(@"export success.*saved as (?'modelRelativePath'.*) \(.*?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ExportResultParserRegex();

    [GeneratedRegex(@"New.*?ultralytics\/(?'version'.*)\savailable.*Update.*pip install", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UltralyticsUpdateAvailableParserRegex();

    [GeneratedRegex(@"^\s*(OSError|fatal|RuntimeError|Error\s*)\:\s*(?'error'.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex OsErrorParserRegex();

    [GeneratedRegex(@"(?:^|\s)(?:--)?workers\s*(?:=|\s+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TrainingWorkersArgumentParserRegex();

    [GeneratedRegex(@"Ultralytics\s+(?'YoloVersion'.*?)\s+(?'PythonVersion'.*?)\s+(?'TorchVersion'.*?)\+(?'DeviceType'.*?)\s+(?'DeviceIndex'[\w\:]+)(?:\s+\((?'DeviceName'.*)\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex YoloChecksParserRegex();

    private readonly FileInfo conversionScript;
    private readonly IPrerequisitesToolchain toolchain;
    private readonly IGpuRuntimeDetector gpuRuntimeDetector;

    public Yolo8CliWrapper(IPrerequisitesToolchain toolchain, IGpuRuntimeDetector gpuRuntimeDetector)
    {
        this.toolchain = toolchain;
        this.gpuRuntimeDetector = gpuRuntimeDetector;
        var conversionScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "ConvertCVATtoYolo8.py");
        conversionScript = new FileInfo(conversionScriptPath);
        if (!conversionScript.Exists)
        {
            throw new FileNotFoundException(message: $"Conversion script not found @ {conversionScript.FullName}");
        }
        
        Environment.SetEnvironmentVariable("YOLO_OFFLINE", "True");
        Environment.SetEnvironmentVariable("YOLO_AUTOINSTALL", "False");
    }

    private Command CreatePythonCommand()
    {
        return WithManagedPythonEnvironment(Cli.Wrap(toolchain.RequireVenvPythonExecutable().FullName));
    }

    private Command CreateYoloCommand(Action<EnvironmentVariablesBuilder>? configureEnvironment = null)
    {
        return Cli.Wrap(toolchain.RequireYoloExecutable().FullName)
            .WithEnvironmentVariables(x =>
            {
                SetManagedPythonEnvironment(x);
                configureEnvironment?.Invoke(x);
            });
    }

    public async Task ConvertAnnotationsToYolo8FromCvat(
        Yolo8ConvertAnnotationsArguments settings,
        Action<YoloCommandOutput>? outputHandler = null)
    {
        if (settings.OutputDirectory.Exists)
        {
            settings.OutputDirectory.Delete(recursive: true);
        }

        if (!File.Exists(conversionScript.FullName))
        {
            throw new FileNotFoundException($"Conversion script not found @ {conversionScript.FullName}", conversionScript.Name);
        }
        
        TempFile tmpFileListFile = null;
        if (settings.Annotations.Length > 10)
        {
            tmpFileListFile = new TempFile();
            await File.WriteAllLinesAsync(tmpFileListFile.File.FullName, settings.Annotations.Select(x => x.FullName).ToArray(), CancellationToken.None);
        }

        var cmd = CreatePythonCommand()
            .WithArguments(x =>
            {
                x.Add($"\"{conversionScript.FullName}\"", escape: false);
                if (settings.UseSymlinks)
                {
                    x.Add($"--symlinks", escape: false);
                }

               
                x.Add($"--outputDirectory", escape: false);
                x.Add($"\"{settings.OutputDirectory.FullName}\"", escape: false);
                x.Add($"--trainPercentage", escape: false);
                x.Add($"{settings.TrainValPercentage}", escape: false);
                
                if (tmpFileListFile == null)
                {
                    x.Add($"--inputAnnotationsFiles", escape: false);
                    x.Add(string.Join(" ", settings.Annotations.Select(x => $"\"{x.FullName}\"")), escape: false);
                }
                else
                {
                    x.Add($"--inputAnnotationsFileList \"{tmpFileListFile.File.FullName}\"", escape: false);
                }
            });

        ReportCommandStart(cmd, outputHandler);
        await foreach (var cmdEvent in cmd.ListenAndLogAsync())
        {
            CaptureCommandEvent(cmdEvent, outputHandler);
        }

        settings.OutputDirectory.Refresh();
        if (!settings.OutputDirectory.Exists)
        {
            throw new InvalidOperationException($"Failed to execute command, directory not found: {settings.OutputDirectory.FullName}");
        }
        
        if (tmpFileListFile != null)
        {
            //I purposefully do not use try.finally or using
            //to make it so if some error occurs, TMP file will still be in place to test things out
            //yes, it will leave minor trash behind
            Log.Info($"Cleaning up tmp file @ {tmpFileListFile}");
            tmpFileListFile.Dispose();
        }
    }

    public async Task UpdateYolo(
        CancellationToken cancellationToken = default,
        Action<YoloCommandOutput>? outputHandler = default)
    {
        Log.Debug($"Running yolo8 update via pip");
        var cmd = CreatePythonCommand()
            .WithArguments(x =>
            {
                //pip install -U ultralytics
                x.Add("-m");
                x.Add("pip");
                x.Add("install");
                x.Add("-U");
                x.Add("ultralytics");
            });

        ReportCommandStart(cmd, outputHandler);
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
        {
            CaptureCommandEvent(cmdEvent, outputHandler);
        }
    }

    public async Task<Yolo8ChecksResult> RunChecks(
        CancellationToken cancellationToken = default,
        Action<YoloCommandOutput>? outputHandler = default)
    {
        Log.Debug($"Running yolo8 checks");
        var cmd = CreateYoloCommand()
            .WithArguments(x => { x.Add($"checks", escape: false); });

        var checksParser = YoloChecksParserRegex();

        Yolo8ChecksResult checksResult = default;
        ReportCommandStart(cmd, outputHandler);
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
        {
            var text = CaptureCommandEvent(cmdEvent, outputHandler);

            var checksMatch = checksParser.Match(text);
            if (checksMatch.Success)
            {
                checksResult = new Yolo8ChecksResult()
                {
                    YoloVersion = checksMatch.Groups["YoloVersion"].Value,
                    PythonVersion = checksMatch.Groups["PythonVersion"].Value,
                    TorchVersion = checksMatch.Groups["TorchVersion"].Value,
                    DeviceIndex = checksMatch.Groups["DeviceIndex"].Value,
                    DeviceType = checksMatch.Groups["DeviceType"].Value,
                    DeviceName = checksMatch.Groups["DeviceName"].Value,
                };
            }
        }

        if (checksResult == null)
        {
            throw new InvalidOperationException("Failed to run checks");
        }

        return checksResult;
    }

    public async Task<DirectoryInfo> Predict(
        Yolo8PredictArguments settings,
        CancellationToken cancellationToken = default,
        Action<Yolo8PredictProgressUpdate>? updateHandler = default,
        Action<YoloCommandOutput>? outputHandler = default)
    {
        var workingDirectory = settings.WorkingDirectory;
        if (!workingDirectory.Exists)
        {
            workingDirectory.Create();
        }

        Log.Debug($"Running prediction using model {settings.Model}, output directory: {workingDirectory}");
        var outputDirectory = workingDirectory.GetSubdirectory("runs");

        var cmd = CreateYoloCommand()
            .WithArguments(x =>
            {
                x.Add($"predict", escape: false);
                x.Add($"save_txt=true", escape: false);
                x.Add($"save_conf=true", escape: false);
                x.Add($"project=\"{outputDirectory}\"", escape: false);

                x.Add($"model=\"{settings.Model}\"", escape: false);
                x.Add($"source=\"{settings.Source}\"", escape: false);

                if (settings.Confidence != null)
                {
                    x.Add($"conf={settings.Confidence.Value.ToString(CultureInfo.InvariantCulture)}", escape: false);
                }

                if (settings.IoU != null)
                {
                    x.Add($"iou={settings.IoU.Value.ToString(CultureInfo.InvariantCulture)}", escape: false);
                }

                if (!string.IsNullOrEmpty(settings.ImageSize))
                {
                    x.Add($"imgsz={settings.ImageSize}", escape: false);
                }

                if (!string.IsNullOrEmpty(settings.AdditionalArguments))
                {
                    x.Add(settings.AdditionalArguments, escape: false);
                }
            })
            .WithWorkingDirectory(workingDirectory.FullName);

        var progressParser = PredictProgressParserRegex();
        var updateParser = UltralyticsUpdateAvailableParserRegex();
        var osErrorParser = OsErrorParserRegex();

        ReportCommandStart(cmd, outputHandler);
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
        {
            var text = CaptureCommandEvent(cmdEvent, outputHandler);

            var osErrorMatch = osErrorParser.Match(text);
            if (osErrorMatch.Success)
            {
                var osError = osErrorMatch.Groups["error"];
                Log.Error($"Critical error: {osError}");
                throw new InvalidStateException($"Encountered critical error: {osError}");
            }

            var updateMatch = updateParser.Match(text);
            if (updateMatch.Success)
            {
                var newVersion = updateMatch.Groups["version"].Value;
                Log.Debug($"Detected new Ultralytics {newVersion} update: {text}");
                throw new InvalidStateException($"New Ultralytics Yolo8 version detected: {newVersion}, update by running 'pip install -U ultralytics'");
            }

            var progressMatch = progressParser.Match(text);
            if (progressMatch.Success)
            {
                var progressUpdateRaw = new Yolo8PredictProgressUpdate()
                {
                    ImageCurrent = int.Parse(progressMatch.Groups["ImageCurrent"].Value),
                    ImageMax = int.Parse(progressMatch.Groups["ImageMax"].Value),
                };

                var progressUpdate = progressUpdateRaw with
                {
                    ProgressPercentage = (float) progressUpdateRaw.ImageCurrent / progressUpdateRaw.ImageMax * 100
                };

                Log.Debug($"Progress update: {progressUpdate}");
                updateHandler?.Invoke(progressUpdate);
            }
        }

        var predictOutputDirectory = outputDirectory.GetSubdirectory("predict");
        Log.Debug($"Predict completed, working dir {workingDirectory}, output: {predictOutputDirectory.FullName} (exists: {predictOutputDirectory.Exists})");
        if (!predictOutputDirectory.Exists)
        {
            throw new FileNotFoundException($"Predict failed - output directory not found @ {predictOutputDirectory.FullName}");
        }

        return predictOutputDirectory;
    }

    public async Task<FileInfo> Train(
        Yolo8TrainArguments settings,
        CancellationToken cancellationToken = default,
        Action<Yolo8TrainProgressUpdate> updateHandler = default,
        Action<YoloCommandOutput>? outputHandler = default)
    {
        var dataYaml = new FileInfo(settings.DataYamlPath);
        if (!dataYaml.Exists)
        {
            throw new FileNotFoundException(message: $"Data YAML file not found @ {dataYaml.FullName}");
        }

        var revisionDirectory = settings.OutputDirectory ?? dataYaml.Directory;
        var outputDirectory = revisionDirectory.GetSubdirectory("runs");
        Log.Debug($"Training model, project directory: {outputDirectory}");
        if (Directory.Exists(outputDirectory.FullName) && Directory.EnumerateFiles(outputDirectory.FullName).Any())
        {
            Log.Debug($"Removing non-empty directory {outputDirectory}");
            Directory.Delete(outputDirectory.FullName, recursive: true);
        }

        var progressParser = TrainProgressParserRegex();
        var scanningProgressParser = TrainScanningProgressParserRegex();
        var updateParser = UltralyticsUpdateAvailableParserRegex();
        var osErrorParser = OsErrorParserRegex();
        var managedDeviceArgument = await ResolveTrainingDeviceArgumentAsync(settings.AdditionalArguments, cancellationToken);
        var managedWorkersArgument = ResolveDefaultTrainingWorkersArgument(settings.AdditionalArguments);
        if (!string.IsNullOrWhiteSpace(managedWorkersArgument))
        {
            Log.Info($"Training workers are not specified, using {managedWorkersArgument} to avoid Windows multiprocessing memory pressure");
        }

        var cmd = CreateYoloCommand(x =>
            {
                if (settings.MaxCpuCoresCount > 0)
                {
                    x.Set("NUM_THREADS", settings.MaxCpuCoresCount.ToString());
                }
            })
            .WithArguments(x =>
            {
                x.Add($"task=detect", escape: false);
                x.Add($"mode=train", escape: false);
                x.Add($"plots=true", escape: false);
                x.Add($"project=\"{outputDirectory}\"", escape: false);

                x.Add($"model=\"{settings.Model}\"", escape: false);
                x.Add($"data=\"{settings.DataYamlPath}\"", escape: false);

                if (!string.IsNullOrEmpty(settings.ImageSize))
                {
                    x.Add($"imgsz={settings.ImageSize}", escape: false);
                }

                if (settings.Epochs != null && settings.Epochs.Value > 0)
                {
                    x.Add($"epochs={settings.Epochs.Value.ToString(CultureInfo.InvariantCulture)}", escape: false);
                }

                if (!string.IsNullOrWhiteSpace(managedDeviceArgument))
                {
                    x.Add(managedDeviceArgument, escape: false);
                }

                if (!string.IsNullOrWhiteSpace(managedWorkersArgument))
                {
                    x.Add(managedWorkersArgument, escape: false);
                }

                if (!string.IsNullOrEmpty(settings.AdditionalArguments))
                {
                    x.Add(settings.AdditionalArguments, escape: false);
                }
            })
            .WithWorkingDirectory(dataYaml.DirectoryName);


        string modelRelativePath = default;
        ReportCommandStart(cmd, outputHandler);
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
        {
            var text = CaptureCommandEvent(cmdEvent, outputHandler);

            var osErrorMatch = osErrorParser.Match(text);
            if (osErrorMatch.Success)
            {
                var osError = osErrorMatch.Groups["error"];
                Log.Error($"Critical error: {osError}");
                throw new InvalidStateException($"Encountered critical error: {osError}");
            }

            var updateMatch = updateParser.Match(text);
            if (updateMatch.Success)
            {
                var newVersion = updateMatch.Groups["version"].Value;
                Log.Debug($"Detected new Ultralytics {newVersion} update: {text}");
                throw new InvalidStateException($"New Ultralytics Yolo8 version detected: {newVersion}, update by running 'pip install -U ultralytics'");
            }

            var progressMatch = progressParser.Match(text);
            if (progressMatch.Success)
            {
                var progressUpdateRaw = new Yolo8TrainProgressUpdate()
                {
                    EpochCurrent = int.Parse(progressMatch.Groups["EpochCurrent"].Value),
                    EpochMax = int.Parse(progressMatch.Groups["EpochMax"].Value),
                    EpochPercentage = Int32.Parse(progressMatch.Groups["EpochProgressPercentage"].Value),
                    VideoRAM = progressMatch.Groups["VideoRAM"].Value,
                };

                var progressUpdate = progressUpdateRaw with
                {
                    ProgressPercentage = (float) progressUpdateRaw.EpochCurrent / progressUpdateRaw.EpochMax * 100
                };

                Log.Debug($"Progress update: {progressUpdate}");
                updateHandler?.Invoke(progressUpdate);
            }
            
            var scanningProgressMatch = scanningProgressParser.Match(text);
            if (scanningProgressMatch.Success)
            {
                var fileName = Path.GetFileName(Path.GetDirectoryName(scanningProgressMatch.Groups["path"].Value));
                var fileCurrent = int.Parse(scanningProgressMatch.Groups["ImageCurrent"].Value);
                var fileMax = int.Parse(scanningProgressMatch.Groups["ImageMax"].Value);
                var progressUpdateRaw = new Yolo8TrainProgressUpdate()
                {
                    Text = $"Scanning... {fileName} {fileCurrent}/{fileMax} {((float) fileCurrent / fileMax * 100):F0}%"
                };

                Log.Debug($"Progress update: {progressUpdateRaw}");
                updateHandler?.Invoke(progressUpdateRaw);
            }
        }

        var trainedModels = Directory.GetFiles(outputDirectory.FullName, "*.pt", SearchOption.AllDirectories);
        Log.Debug($"Trained models: {trainedModels.DumpToString()}");

        if (!trainedModels.Any())
        {
            throw new FileNotFoundException($"Failed to find any trained models (*.pt) inside {outputDirectory}");
        }

        var trainedModel = trainedModels.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals("best", StringComparison.OrdinalIgnoreCase));
        if (trainedModel == null)
        {
            throw new FileNotFoundException($"Failed to find 'best' trained model in list of models: {trainedModels.DumpToString()}");
        }
        return new FileInfo(trainedModel);
    }

    internal static string? ResolveDefaultTrainingWorkersArgument(string? additionalArguments)
    {
        return string.IsNullOrWhiteSpace(additionalArguments) || !TrainingWorkersArgumentParserRegex().IsMatch(additionalArguments)
            ? DefaultTrainingWorkersArgument
            : null;
    }

    private async Task<string> ResolveTrainingDeviceArgumentAsync(string additionalArguments, CancellationToken cancellationToken)
    {
        if (gpuRuntimeDetector.HasExplicitDeviceArgument(additionalArguments))
        {
            Log.Debug($"Training device is specified by additional arguments: {additionalArguments}");
            return null;
        }

        try
        {
            var pytorch = await gpuRuntimeDetector.ProbePyTorchAsync(cancellationToken: cancellationToken);
            var deviceArgument = GpuRuntimeDetector.ResolveYoloTrainingDeviceArgument(pytorch, additionalArguments);
            if (string.IsNullOrWhiteSpace(deviceArgument))
            {
                Log.Debug($"PyTorch CUDA is not available, YOLO will use its default CPU-capable device selection: {pytorch.Summary}");
                return null;
            }

            Log.Info($"PyTorch CUDA is available, training will use {deviceArgument}: {pytorch.Summary}");
            return deviceArgument;
        }
        catch (Exception e)
        {
            Log.Warn("Failed to probe PyTorch CUDA state before training; YOLO will use its default device selection", e);
            return null;
        }
    }

    public async Task<FileInfo> Convert(
        Yolo8ExportArguments settings,
        CancellationToken cancellationToken = default,
        Action<YoloCommandOutput>? outputHandler = default)
    {
        var model = new FileInfo(settings.Model);
        if (!model.Exists)
        {
            throw new FileNotFoundException(message: $"Model file not found @ {model.FullName}");
        }

        var outputDirectory = model.Directory;
        Log.Debug($"Converting model to {settings.Format}, output directory: {outputDirectory}");

        var exportResultParser = ExportResultParserRegex();

        var cmd = CreateYoloCommand()
            .WithArguments(x =>
            {
                x.Add($"export", escape: false);

                x.Add($"model=\"{settings.Model}\"", escape: false);
                x.Add($"format=\"{settings.Format}\"", escape: false);
                if (settings.Opset != null)
                {
                    x.Add($"opset=\"{settings.Opset}\"", escape: false);
                }
            })
            .WithWorkingDirectory(outputDirectory.FullName);

        string convertedModelPath = default;
        ReportCommandStart(cmd, outputHandler);
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken))
        {
            var text = CaptureCommandEvent(cmdEvent, outputHandler);

            var resultMatch = exportResultParser.Match(text);
            if (resultMatch.Success)
            {
                convertedModelPath = resultMatch.Groups["modelRelativePath"].Value.Trim('\'');
                Log.Debug($"Converted model, extracted path: {convertedModelPath} (is relative: {!Path.IsPathRooted(convertedModelPath)})");
            }
        }


        if (string.IsNullOrEmpty(convertedModelPath))
        {
            throw new InvalidOperationException("Failed to extract path to a converted model");
        }

        var convertedModel = Path.IsPathRooted(convertedModelPath)
            ? new FileInfo(Path.Combine(outputDirectory.FullName, convertedModelPath))
            : new FileInfo(convertedModelPath);
        Log.Debug($"Converted model path: {convertedModel.FullName} (exists: {convertedModel.Exists})");
        if (!convertedModel.Exists)
        {
            throw new FileNotFoundException($"Converted model not found @ {convertedModel.FullName}");
        }

        return convertedModel;
    }

    private static void ReportCommandStart(Command command, Action<YoloCommandOutput>? outputHandler)
    {
        outputHandler?.Invoke(YoloCommandOutput.Info($"Running command: {command}"));
    }

    private static string CaptureCommandEvent(CommandEvent cmdEvent, Action<YoloCommandOutput>? outputHandler)
    {
        switch (cmdEvent)
        {
            case StartedCommandEvent started:
                outputHandler?.Invoke(YoloCommandOutput.Info($"Process started: {started.ProcessId}"));
                return string.Empty;

            case StandardOutputCommandEvent stdOut:
                outputHandler?.Invoke(YoloCommandOutput.Output(stdOut.Text));
                return stdOut.Text;

            case StandardErrorCommandEvent stdErr:
                outputHandler?.Invoke(YoloCommandOutput.Error(stdErr.Text));
                return stdErr.Text;

            case ExitedCommandEvent exited:
                outputHandler?.Invoke(YoloCommandOutput.Info($"Process exited: {exited.ExitCode}"));
                return string.Empty;

            default:
                return string.Empty;
        }
    }

    private Command WithManagedPythonEnvironment(Command command)
    {
        return command.WithEnvironmentVariables(SetManagedPythonEnvironment);
    }

    private void SetManagedPythonEnvironment(EnvironmentVariablesBuilder environment)
    {
        ManagedPythonEnvironment.Apply(toolchain, environment, activateVenv: true);
    }
}
