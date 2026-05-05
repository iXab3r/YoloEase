using System.Linq;
using System.Threading;
using ByteSizeLib;
using JetBrains.Annotations;
using PoeShared.Dialogs.Services;
using PoeShared.Services;
using YoloEase.UI.Dto;
using YoloEase.UI.Services;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.Core;

/// <summary>
/// Creates YOLO datasets from annotation XML and tracks dataset generation settings.
/// </summary>
public class Yolo8DatasetAccessor : RefreshableReactiveObject
{
    private readonly IConfigSerializer configSerializer;
    private readonly Yolo8CliWrapper cliWrapper;
    private readonly IUniqueIdGenerator idGenerator;
    private readonly IOpenFileDialog openFileDialog;
    private readonly IYoloModelCachingService modelCachingService;
    private readonly IScheduler uiScheduler;

    private readonly SourceCache<TrainedModelFileInfo, string> modelFileSource = new(x => x.ModelFile.FullName);
    private readonly IObservableCache<DatasetInfo, string> datasetsSource;
    private readonly SourceCache<DatasetInfo, string> storageDatasetsSource = new(x => x.IndexFile.FullName);
    private readonly SourceCache<DatasetInfo, string> nonStorageDatasetsSource = new(x => x.IndexFile.FullName);

    private static readonly Binder<Yolo8DatasetAccessor> Binder = new();

    static Yolo8DatasetAccessor()
    {
        Binder.Bind(x => x.StorageDirectory == null ? null : new DirectoryInfo(Path.Combine(x.StorageDirectory.FullName, "datasets")))
            .To(x => x.DatasetsDirectory);
    }

    public Yolo8DatasetAccessor(
        IConfigSerializer configSerializer,
        Yolo8CliWrapper cliWrapper,
        IUniqueIdGenerator idGenerator,
        IOpenFileDialog openFileDialog,
        IFileAssetsAccessor assets,
        IYoloModelCachingService modelCachingService,
        [Dependency(WellKnownSchedulers.UI)] IScheduler uiScheduler)
    {
        this.configSerializer = configSerializer;
        this.cliWrapper = cliWrapper;
        this.idGenerator = idGenerator;
        this.openFileDialog = openFileDialog;
        this.modelCachingService = modelCachingService;
        this.uiScheduler = uiScheduler;
        Assets = assets;

        datasetsSource = storageDatasetsSource
            .Connect()
            .Or(nonStorageDatasetsSource.Connect())
            .AsObservableCache();

        TrainedModels = modelFileSource.Connect().RemoveKey().ToSourceListEx().AddTo(Anchors);
        Datasets = datasetsSource.Connect().RemoveKey().ToSourceListEx().AddTo(Anchors);
        Binder.Attach(this).AddTo(Anchors);
    }

    public DirectoryInfo? StorageDirectory { get; set; }

    public DirectoryInfo? DatasetsDirectory { get; [UsedImplicitly] private set; }

    public IObservableListEx<TrainedModelFileInfo> TrainedModels { get; }

    public IObservableListEx<DatasetInfo> Datasets { get; }

    public int Epochs { get; set; }

    public string TrainAdditionalArguments { get; set; }
    public int MaxNumberOfCpuCores { get; set; }

    public int ModelSize { get; set; } = 640;
    
    public float TrainValSplitPercentage { get; set; } = 80;

    public string BaseModelPath { get; set; }

    public IFileAssetsAccessor Assets { get; }

    public ModelTrainingSettings TrainingSettings => new ModelTrainingSettings()
    {
        Epochs = Epochs,
        ModelSize = ModelSize.ToString(),
        Model = BaseModelPath
    };

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        var storageDatasets = await Task.Run(ScanStorageDatasets);
        storageDatasetsSource.EditDiff(storageDatasets);

        var datasets = datasetsSource.Items.ToArray();
        var trainedModels = await Task.Run(() => ScanTrainedModels(datasets));
        modelFileSource.EditDiff(trainedModels);
    }

    private IReadOnlyList<DatasetInfo> ScanStorageDatasets()
    {
        var datasetsDirectory = DatasetsDirectory;
        var storageDirectory = StorageDirectory;
        if (datasetsDirectory == null || storageDirectory == null)
        {
            return Array.Empty<DatasetInfo>();
        }

        try
        {
            datasetsDirectory.Refresh();
            if (!datasetsDirectory.Exists)
            {
                return Array.Empty<DatasetInfo>();
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to inspect datasets directory {datasetsDirectory.FullName}", e);
            return Array.Empty<DatasetInfo>();
        }

        var datasets = new List<DatasetInfo>();
        DirectoryInfo[] datasetDirectories;
        try
        {
            datasetDirectories = datasetsDirectory.GetDirectories();
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to enumerate dataset directories under {datasetsDirectory.FullName}", e);
            return Array.Empty<DatasetInfo>();
        }

        foreach (var directory in datasetDirectories)
        {
            try
            {
                var indexFiles = directory.GetFiles("data.yaml", SearchOption.AllDirectories);
                if (indexFiles.Length <= 0)
                {
                    continue;
                }

                if (indexFiles.Length > 1)
                {
                    Log.Warn($"Directory {directory} contains more than one index file: {indexFiles.Select(x => x.FullName).DumpToString()}");
                    continue;
                }

                var indexFile = indexFiles[0];
                var datasetInfo = DatasetInfo.FromIndexFile(indexFile, storageDirectory, configSerializer);
                datasets.Add(datasetInfo);
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to read dataset metadata from {directory.FullName}", e);
            }
        }

        return datasets;
    }

    public async Task<DatasetInfo> CreateAnnotatedDataset(
        IEnumerable<FileInfo> annotationsSource,
        AnnotationProjectAccessor project,
        Action<YoloCommandOutput>? outputHandler = null)
    {
        var trainId = $"{idGenerator.Next()}";
        var datasetDirectory = new DirectoryInfo(Path.Combine(DatasetsDirectory.FullName, trainId));

        var annotations = annotationsSource.ToArray();
        if (!annotations.Any())
        {
            throw new ArgumentException("At least one annotation must be specified");
        }

        await cliWrapper.ConvertAnnotationsToYolo8FromCvat(new Yolo8ConvertAnnotationsArguments()
        {
            Annotations = annotations,
            OutputDirectory = datasetDirectory,
            UseSymlinks = true,
            TrainValPercentage = (int)TrainValSplitPercentage,
        }, outputHandler);

        var indexFile = new FileInfo(Path.Combine(datasetDirectory.FullName, "data.yaml"));
        if (!indexFile.Exists)
        {
            throw new FileNotFoundException($"Failed to find index file at {indexFile.FullName}");
        }

        var projectInfoPath = new FileInfo(Path.Combine(datasetDirectory.FullName, "cvataat.json"));
        var images = LocalStorageAssetsAccessor.FilesFilter
            .Select(x => datasetDirectory.GetFiles(x, SearchOption.AllDirectories))
            .SelectMany(x => x).Select(x => x.Name)
            .ToArray();

        var projectInfo = new YoloEaseProjectInfo()
        {
            Files = images,
            Revision = 0,
            ModelTrainingSettings = new ModelTrainingSettings()
            {
                Model = BaseModelPath,
                Epochs = Epochs,
                ModelSize = ModelSize.ToString()
            },
            ProjectId = project.ProjectId,
            ServerUrl = project.ServerUrl,
            ProjectUrl = project.ResolveProjectUrl(project.ProjectId),
            ProjectName = project.ProjectName,
            OrganizationName = project.OrganizationName,
            OrganizationId = project.OrganizationId,
        };
        var projectInfoContent = configSerializer.Serialize(projectInfo);
        await File.WriteAllTextAsync(projectInfoPath.FullName, projectInfoContent);

        var datasetInfo = DatasetInfo.FromIndexFile(indexFile, StorageDirectory, configSerializer);
        storageDatasetsSource.AddOrUpdate(datasetInfo);
        return datasetInfo;
    }

    private IReadOnlyList<TrainedModelFileInfo> ScanTrainedModels(IReadOnlyList<DatasetInfo> datasets)
    {
        var modelFiles = new List<TrainedModelFileInfo>();
        foreach (var training in datasets)
        {
            try
            {
                if (!File.Exists(training.IndexFile.FullName))
                {
                    continue;
                }

                var directory = training.IndexFile.Directory;
                if (directory == null)
                {
                    continue;
                }

                directory.Refresh();
                if (!directory.Exists)
                {
                    continue;
                }

                var models = directory.GetFiles("*.onnx", SearchOption.AllDirectories);
                foreach (var model in models)
                {
                    modelFiles.Add(new TrainedModelFileInfo
                    {
                        ModelFile = model,
                    });
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Failed to scan trained models for dataset {training.IndexFile.FullName}", e);
            }
        }

        return modelFiles;
    }

    public async Task AddDataset()
    {
        uiScheduler.Schedule(() =>
        {
            if (string.IsNullOrEmpty(openFileDialog.InitialDirectory))
            {
                openFileDialog.InitialDirectory = StorageDirectory.FullName;
            }

            openFileDialog.Filter = "data.yaml|*.yaml";
            var result = openFileDialog.ShowDialog();
            if (result != null)
            {
                var datasetInfo = DatasetInfo.FromIndexFile(result, StorageDirectory, configSerializer);
                nonStorageDatasetsSource.AddOrUpdate(datasetInfo);
            }
        });
    }

    public async Task RemoveStorageDataset(DatasetInfo datasetInfo)
    {
        datasetInfo.IndexFile.Directory?.Delete(recursive: true);
        storageDatasetsSource.Remove(datasetInfo);
        await Refresh();
    }

    public async Task RemoveNonStorageDataset(DatasetInfo datasetInfo)
    {
        nonStorageDatasetsSource.Remove(datasetInfo);
        await Refresh();
    }


    public Task<Yolo8ChecksResult> RunChecks(
        CancellationToken cancellationToken = default,
        Action<YoloCommandOutput>? outputHandler = null)
    {
        return this.cliWrapper.RunChecks(cancellationToken, outputHandler);
    }

    public async Task<FileInfo> TrainModel(
        DatasetInfo dataset,
        Action<Yolo8TrainProgressUpdate> updateHandler = null,
        CancellationToken cancellationToken = default,
        Action<YoloCommandOutput>? outputHandler = null)
    {
        var now = DateTime.Now;
        var datasetIndexFile = dataset.IndexFile;
        using var log = new BenchmarkTimer($"Train {datasetIndexFile.FullName}", Log).WithLoggingOnDisposal();
        log.Step($"Initializing training, dataset: {datasetIndexFile.FullName} (exists: {datasetIndexFile.Exists})");

        if (!datasetIndexFile.Exists)
        {
            throw new DirectoryNotFoundException($"Index file not found @ {datasetIndexFile.FullName}");
        }

        if (dataset.ImagesTrainingCount <= 0 || dataset.ImagesValidationCount <= 0)
        {
            throw new ArgumentException($"Dataset is not valid, expected to have at least 1 training and 1 validation image: {dataset}");
        }

        var outputDirectory = datasetIndexFile.Directory!;

        log.Step($"Resolving model {BaseModelPath}");
        var cachedModelFile = await modelCachingService.ResolveModelByName(BaseModelPath);
        log.Step($"Resolved cached model {BaseModelPath}: {cachedModelFile.FullName}");
        var modelFilePath = Path.Combine(outputDirectory.FullName, cachedModelFile.Name);
        File.CreateSymbolicLink(modelFilePath, cachedModelFile.FullName);
        var modelFile = new FileInfo(modelFilePath);
        log.Step($"Actual model path: {modelFile.FullName}");

        var dataYamlFiles = outputDirectory.GetFiles("data.yaml", SearchOption.TopDirectoryOnly);
        if (dataYamlFiles.Length <= 0)
        {
            throw new FileNotFoundException($"Data.yaml not found in {outputDirectory.FullName}");
        }

        var dataYamlFile = dataYamlFiles.Single();
        var dataYamlAge = now - dataYamlFile.LastWriteTime;
        log.Step($"Data YAML: {dataYamlFile.FullName} ({ByteSize.FromBytes(dataYamlFile.Length)}, modified: {dataYamlFile.LastWriteTime} {dataYamlAge.TotalSeconds}s ago)");

        log.Step("Starting training");
        var trainedModel = await cliWrapper.Train(new Yolo8TrainArguments()
        {
            Epochs = Epochs,
            Model = modelFile.FullName,
            ImageSize = $"{ModelSize}",
            DataYamlPath = dataYamlFile.FullName,
            OutputDirectory = outputDirectory,
            MaxCpuCoresCount = MaxNumberOfCpuCores,
            AdditionalArguments = TrainAdditionalArguments
        }, updateHandler: updateHandler, cancellationToken: cancellationToken, outputHandler: outputHandler);
        log.Step($"Successfully trained model: {trainedModel.FullName} ({ByteSize.FromBytes(trainedModel.Length)})");

        log.Step("Converting model to ONNX");
        var convertedModel = await cliWrapper.Convert(new Yolo8ExportArguments()
        {
            Format = "onnx",
            Model = trainedModel.FullName,
            Opset = 17
        }, cancellationToken, outputHandler);
        Log.Debug($"Successfully converted model: {convertedModel.FullName} ({ByteSize.FromBytes(convertedModel.Length)})");
        log.Step("Conversion completed, model is ready to use");

        await Refresh();

        return convertedModel;
    }
}
