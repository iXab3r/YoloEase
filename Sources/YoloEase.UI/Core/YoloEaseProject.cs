using YoloEase.UI.Augmentations;
using YoloEase.UI.Services;

namespace YoloEase.UI.Core;

public class YoloEaseProject : RefreshableReactiveObject
{
    private static readonly Binder<YoloEaseProject> Binder = new();

    private readonly IYoloModelCachingService yoloModelCachingService;

    static YoloEaseProject()
    {
        Binder.Bind(x => x.StorageDirectory).To(x => x.TrainingDataset.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.Assets.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.Predictions.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.yoloModelCachingService.StorageDirectory);
    }

    public YoloEaseProject(
        CvatProjectAccessor cvatProjectAccessor,
        IYoloModelCachingService yoloModelCachingService,
        IFactory<DataSourcesProvider> fileSystemAssetsAccessor,
        IFactory<Yolo8PredictAccessor> predictAccessorFactory,
        IFactory<AugmentationsAccessor, AnnotationsAccessor> augmentationsAccessorFactory,
        IFactory<LocalStorageAssetsAccessor, DataSourcesProvider> localStorageDatasetAccessorFactory,
        IFactory<Yolo8DatasetAccessor, IFileAssetsAccessor> trainingDatasetAccessorFactory,
        IFactory<AnnotationsAccessor,  CvatProjectAccessor, IFileAssetsAccessor, Yolo8DatasetAccessor> annotationsAccessorFactory,
        IFactory<TrainingBatchAccessor, CvatProjectAccessor, IFileAssetsAccessor> batchAccessorFactory)
    {
        this.yoloModelCachingService = yoloModelCachingService;
        RemoteProject = cvatProjectAccessor.AddTo(Anchors);
        DataSources = fileSystemAssetsAccessor.Create().AddTo(Anchors);
        Predictions = predictAccessorFactory.Create().AddTo(Anchors);
        Assets = localStorageDatasetAccessorFactory.Create(DataSources).AddTo(Anchors);
        TrainingDataset = trainingDatasetAccessorFactory.Create(Assets).AddTo(Anchors);
        TrainingBatch = batchAccessorFactory.Create(cvatProjectAccessor, Assets).AddTo(Anchors);
        Annotations = annotationsAccessorFactory.Create(cvatProjectAccessor, Assets, TrainingDataset).AddTo(Anchors);
        Augmentations = augmentationsAccessorFactory.Create(Annotations).AddTo(Anchors);
        Binder.Attach(this).AddTo(Anchors);
    }
    
    public DirectoryInfo StorageDirectory { get; set; }
    
    public CvatProjectAccessor RemoteProject { get; }
    
    public AugmentationsAccessor Augmentations { get; }
    
    public LocalStorageAssetsAccessor Assets { get; }
    
    public DataSourcesProvider DataSources { get; }
    
    public Yolo8DatasetAccessor TrainingDataset { get; }
    
    public Yolo8PredictAccessor Predictions { get; }
    
    public TrainingBatchAccessor TrainingBatch { get; }
    
    public AnnotationsAccessor Annotations { get; }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        await Assets.Refresh();
        await RemoteProject.Refresh();
        await Annotations.Refresh();
        await TrainingBatch.Refresh();
        await TrainingDataset.Refresh();
    }
}