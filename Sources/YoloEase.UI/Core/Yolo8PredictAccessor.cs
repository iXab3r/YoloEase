using System.Globalization;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.ML.OnnxRuntime;
using PoeShared.Dialogs.Services;
using YoloEase.UI.Dto;
using YoloEase.UI.Scaffolding;
using YoloEase.UI.TrainingTimeline;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.Core;



public class Yolo8PredictAccessor : RefreshableReactiveObject
{
    private static readonly Binder<Yolo8PredictAccessor> Binder = new();

    static Yolo8PredictAccessor()
    {
    }

    private readonly Yolo8CliWrapper cliWrapper;
    private readonly IOpenFileDialog openFileDialog;
    private readonly IScheduler uiScheduler;
    private static readonly string[] ImageFilesExtensions = new[] {"*.png", "*.jpg", "*.bmp"};

    public Yolo8PredictAccessor(
        Yolo8CliWrapper cliWrapper,
        IOpenFileDialog openFileDialog,
        [Dependency(WellKnownSchedulers.UI)] IScheduler uiScheduler)
    {
        this.cliWrapper = cliWrapper;
        this.openFileDialog = openFileDialog;
        this.uiScheduler = uiScheduler;

        Binder.Attach(this).AddTo(Anchors);
    }

    public string PredictAdditionalArguments { get; set; }

    public DirectoryInfo StorageDirectory { get; [UsedImplicitly] set; }

    public TrainedModelFileInfo PredictionModel { get; set; }

    public DatasetPredictInfo LatestPredictions { get; set; }
    
    public float ConfidenceThresholdPercentage { get; set; } = 25f;
    
    public float IoUThresholdPercentage { get; set; } = 70f;

    public async Task SelectModel()
    {
        uiScheduler.Schedule(() =>
        {
            openFileDialog.Filter = "ONNX model|*.onnx|PyTorch model|*.pt|All files|*.*";
            var loaded = this.PredictionModel;
            if (loaded != null)
            {
                openFileDialog.InitialDirectory = loaded.ModelFile.DirectoryName;
            }
            var result = openFileDialog.ShowDialog();
            if (result != null)
            {
                LoadModel(result);
            }
        });
    }

    public void LoadModel(FileInfo fileInfo)
    {
        PredictionModel = new TrainedModelFileInfo()
        {
            ModelFile = fileInfo
        };
    }

    private async Task<DatasetPredictInfo> GetPredictionsOrDefault(TrainedModelFileInfo modelFileInfo, DirectoryInfo predictionsDir, YoloModelDescription yoloModelDescription)
    {
        var modelDirectory = GetModelPredictionsFolder(modelFileInfo);
        if (!modelDirectory.Exists)
        {
            return null;
        }

        if (!predictionsDir.Exists)
        {
            return null;
        }
        var predictions = ParsePredictions(predictionsDir, yoloModelDescription).ToArray();
        return new DatasetPredictInfo()
        {
            OutputDirectory = modelDirectory,
            Predictions = predictions,
            ModelFile = modelFileInfo
        };
    }

    public async Task<DatasetPredictInfo> Predict(
        PredictArgs args,
        Action<Yolo8PredictProgressUpdate> updateHandler = null,
        CancellationToken cancellationToken = default)
    {
        var modelFile = args.Model.ModelFile;
        if (!modelFile.Exists)
        {
            throw new DirectoryNotFoundException($"Model file not found @ {modelFile.FullName}");
        }

        Log.Info($"Loading model from file {modelFile.FullName}");
        var modelData = await File.ReadAllBytesAsync(modelFile.FullName, cancellationToken);
        var modelOptions = new SessionOptions();
        using var yoloModel = new YoloModel(modelData, modelOptions);
        Log.Info($"Loaded model from file {modelFile.FullName}: {yoloModel.Description.Dump()}");
        
        var modelDirectory = GetModelPredictionsFolder(args.Model);
        if (modelDirectory.Exists)
        {
            modelDirectory.Delete(recursive: true);
        }

        modelDirectory.Create();

        var tmpInputDirectoryPath = modelDirectory.FullName + ".tmp";
        if (Directory.Exists(tmpInputDirectoryPath))
        {
            Directory.Delete(tmpInputDirectoryPath, recursive: true);
        }
        Directory.CreateDirectory(tmpInputDirectoryPath);

        foreach (var file in args.Files)
        {
            var linkFilePath = Path.Combine(tmpInputDirectoryPath, file.Name);
            File.CreateSymbolicLink(linkFilePath, file.FullName);
        }
        
        var predictDirectory = await cliWrapper.Predict(new Yolo8PredictArguments()
        {
            Model = modelFile.FullName,
            WorkingDirectory = modelDirectory,
            Source = tmpInputDirectoryPath,
            Confidence = ConfidenceThresholdPercentage / 100,
            IoU = IoUThresholdPercentage / 100,
            ImageSize = yoloModel.Description.Size.Width.ToString(),
            AdditionalArguments = PredictAdditionalArguments,
        }, updateHandler: updateHandler, cancellationToken: cancellationToken);

        return await GetPredictionsOrDefault(args.Model, predictDirectory, yoloModel.Description);
    }

    private static IEnumerable<PredictInfo> ParsePredictions(
        DirectoryInfo predictDirectory,
        YoloModelDescription modelDescription)
    {
        var result = new List<PredictInfo>();
        var labelsDirectory = new DirectoryInfo(Path.Combine(predictDirectory.FullName, "labels"));
        if (!labelsDirectory.Exists)
        {
            return Array.Empty<PredictInfo>();
        }
        
        if (modelDescription.Labels.IsEmpty())
        {
            throw new ArgumentException($"Model does not contain valid labels");
        }
        var labelsByClassIdx = modelDescription.Labels.ToDictionary(x => x.Id, x => x);

        var predictImages = predictDirectory.GetFiles("*.*", SearchOption.TopDirectoryOnly);
        foreach (var predictImage in predictImages)
        {
            var imageSize = ImageUtils.GetImageSize(predictImage);
            var labels = new List<YoloPredictionInfo>();
            var labelFileName = new FileInfo(Path.Combine(labelsDirectory.FullName, Path.ChangeExtension(predictImage.Name, "txt")));
            if (labelFileName.Exists)
            {
                //unscaled! Yolo uses 0..1 notation for each value
                var yoloLabels = ParseYoloLabels(labelFileName).ToArray();
                foreach (var yoloLabel in yoloLabels)
                {
                    var label = yoloLabel with
                    {
                        BoundingBox = yoloLabel.BoundingBox.Scale(imageSize.Width, imageSize.Height)
                    };
                    labels.Add(label);
                }
            }

            var prediction = new PredictInfo()
            {
                File = predictImage,
                Labels = labels.Select(x =>
                {
                    if (!labelsByClassIdx.TryGetValue(x.ClassIdx, out var label))
                    {
                        throw new ArgumentException($"Failed to map class Idx {x.ClassIdx}, to label, known labels: {labelsByClassIdx.DumpToString()}");
                    }

                    return new YoloPrediction()
                    {
                        BoundingBox = x.BoundingBox,
                        Score = x.Score,
                        Label = label
                    };
                }).ToArray()
            };
            result.Add(prediction);
        }

        return result;
    }

    private static IEnumerable<YoloPredictionInfo> ParseYoloLabels(FileInfo file)
    {
        using var reader = file.OpenText();

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(' ');

            if (parts.Length != 6)
            {
                throw new FormatException("Each line must contain exactly five values.");
            }

            var id = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var centerX = float.Parse(parts[1], CultureInfo.InvariantCulture);
            var centerY = float.Parse(parts[2], CultureInfo.InvariantCulture);
            var width = float.Parse(parts[3], CultureInfo.InvariantCulture);
            var height = float.Parse(parts[4], CultureInfo.InvariantCulture);
            var confidence = float.Parse(parts[5], CultureInfo.InvariantCulture);

            yield return new YoloPredictionInfo
            {
                ClassIdx = id,
                BoundingBox = RectangleD.FromYolo(centerX, centerY, width, height),
                Score = confidence
            };
        }
    }

    public static bool HasAllPredictions(
        IReadOnlyList<FileInfo> filesInInput,
        DatasetPredictInfo predictions)
    {
        var filesInInputByName = filesInInput.ToDictionary(x => x.Name);
        var filesInPredictions = ImageFilesExtensions
            .SelectMany(x => predictions.OutputDirectory.GetFiles(x, SearchOption.AllDirectories)).ToDictionary(x => x.Name);

        var additionalFiles = filesInInputByName.Where(x => !filesInPredictions.ContainsKey(x.Key)).ToArray();
        if (additionalFiles.Any())
        {
            return false;
        }

        return true;
    }

    private DirectoryInfo GetModelPredictionsFolder(TrainedModelFileInfo modelFileInfo)
    {
        var predictionsDirectory = Path.Combine(StorageDirectory.FullName, "predictions");
        var modelDirectory = new DirectoryInfo(Path.Combine(predictionsDirectory,
            Path.GetFileNameWithoutExtension(modelFileInfo.ModelFile.Name)));
        return modelDirectory;
    }
}