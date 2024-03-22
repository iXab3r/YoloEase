using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using CliWrap;
using CliWrap.EventStream;

namespace YoloEase.UI.Yolo;

public sealed record Yolo8ConvertAnnotationsArguments
{
    public DirectoryInfo OutputDirectory { get; init; }
    public FileInfo[] Annotations { get; init; }
    public bool UseSymlinks { get; init; }
}

public sealed partial class Yolo8CliWrapper : DisposableReactiveObjectWithLogger
{
    [GeneratedRegex("\\s*(?'EpochCurrent'\\d+)\\/(?'EpochMax'\\d+)\\s*(?'VideoRAM'[\\w\\.]+).*?(?'EpochProgressPercentage'\\d+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TrainProgressParserRegex();

    [GeneratedRegex(@"image (?'ImageCurrent'\d+)/(?'ImageMax'\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PredictProgressParserRegex();

    [GeneratedRegex(@"export success.*saved as (?'modelRelativePath'.*) \(.*?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ExportResultParserRegex();

    [GeneratedRegex(@"New.*?ultralytics\/(?'version'.*)\savailable.*Update.*pip install", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UltralyticsUpdateAvailableParserRegex();

    [GeneratedRegex(@"^\s*(OSError|fatal|RuntimeError|Error\s*)\:\s*(?'error'.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex OsErrorParserRegex();

    [GeneratedRegex(@"Ultralytics\s+(?'YoloVersion'.*?)\s+(?'PythonVersion'.*?)\s+(?'TorchVersion'.*?)\+(?'DeviceType'.*?)\s+(?'DeviceIndex'[\w\:]+)(?:\s+\((?'DeviceName'.*)\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex YoloChecksParserRegex();

    private readonly FileInfo conversionScript;

    public Yolo8CliWrapper()
    {
        var conversionScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "ConvertCVATtoYolo8.py");
        conversionScript = new FileInfo(conversionScriptPath);
        if (!conversionScript.Exists)
        {
            throw new FileNotFoundException(message: $"Conversion script not found @ {conversionScript.FullName}");
        }
    }

    public async Task ConvertAnnotationsToYolo8FromCvat(Yolo8ConvertAnnotationsArguments settings)
    {
        if (settings.OutputDirectory.Exists)
        {
            settings.OutputDirectory.Delete(recursive: true);
        }

        if (!File.Exists(conversionScript.FullName))
        {
            throw new FileNotFoundException($"Conversion script not found @ {conversionScript.FullName}", conversionScript.Name);
        }

        using var tmpScriptFile = new TempFile(conversionScript);

        var cmd = Cli.Wrap("python")
            .WithArguments(x =>
            {
                x.Add($"\"{tmpScriptFile.File.FullName}\"", escape: false);
                if (settings.UseSymlinks)
                {
                    x.Add($"--symlinks", escape: false);
                }

                x.Add($"--inputAnnotationsFiles", escape: false);
                x.Add(string.Join(" ", settings.Annotations.Select(x => $"\"{x.FullName}\"")), escape: false);
                x.Add($"--outputDirectory", escape: false);
                x.Add($"\"{settings.OutputDirectory.FullName}\"", escape: false);
            });

        await foreach (var cmdEvent in cmd.ListenAndLogAsync())
        {
            switch (cmdEvent)
            {
                case StandardOutputCommandEvent stdOut:

                    break;
            }
        }

        settings.OutputDirectory.Refresh();
        if (!settings.OutputDirectory.Exists)
        {
            throw new InvalidOperationException($"Failed to execute command, directory not found: {settings.OutputDirectory.FullName}");
        }
    }

    public async Task UpdateYolo(CancellationToken cancellationToken = default)
    {
        Log.Debug($"Running yolo8 update via pip");
        var cmd = Cli.Wrap("pip")
            .WithArguments(x =>
            {
                //pip install -U ultralytics
                x.Add($"install -U ultralytics", escape: false);
            });

        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
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
        }
    }

    public async Task<Yolo8ChecksResult> RunChecks(CancellationToken cancellationToken = default)
    {
        Log.Debug($"Running yolo8 checks");
        var cmd = Cli.Wrap("yolo")
            .WithArguments(x => { x.Add($"checks", escape: false); });

        var checksParser = YoloChecksParserRegex();

        Yolo8ChecksResult checksResult = default;
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
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

    public async Task<DirectoryInfo> Predict(Yolo8PredictArguments settings, CancellationToken cancellationToken = default, Action<Yolo8PredictProgressUpdate> updateHandler = default)
    {
        var workingDirectory = settings.WorkingDirectory;
        if (!workingDirectory.Exists)
        {
            workingDirectory.Create();
        }

        Log.Debug($"Running prediction using model {settings.Model}, output directory: {workingDirectory}");
        var outputDirectory = workingDirectory.GetSubdirectory("runs");

        var cmd = Cli.Wrap("yolo")
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

        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
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

    public async Task<FileInfo> Train(Yolo8TrainArguments settings, CancellationToken cancellationToken = default, Action<Yolo8TrainProgressUpdate> updateHandler = default)
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
        var updateParser = UltralyticsUpdateAvailableParserRegex();
        var osErrorParser = OsErrorParserRegex();

        var cmd = Cli.Wrap("yolo")
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

                if (!string.IsNullOrEmpty(settings.AdditionalArguments))
                {
                    x.Add(settings.AdditionalArguments, escape: false);
                }
            })
            .WithWorkingDirectory(dataYaml.DirectoryName);


        string modelRelativePath = default;
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken: cancellationToken))
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

    public async Task<FileInfo> Convert(Yolo8ExportArguments settings, CancellationToken cancellationToken = default)
    {
        var model = new FileInfo(settings.Model);
        if (!model.Exists)
        {
            throw new FileNotFoundException(message: $"Model file not found @ {model.FullName}");
        }

        var outputDirectory = model.Directory;
        Log.Debug($"Converting model to {settings.Format}, output directory: {outputDirectory}");

        var exportResultParser = ExportResultParserRegex();

        var cmd = Cli.Wrap("yolo")
            .WithArguments(x =>
            {
                x.Add($"export", escape: false);

                x.Add($"model=\"{settings.Model}\"", escape: false);
                x.Add($"format=\"{settings.Format}\"", escape: false);
            })
            .WithWorkingDirectory(outputDirectory.FullName);

        string convertedModelPath = default;
        await foreach (var cmdEvent in cmd.ListenAndLogAsync(cancellationToken))
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
}