using YoloEase.UI.Augmentations;
using YoloEase.UI.Services;
using YoloEase.UI.TaskAnnotation;

namespace YoloEase.UI.Core;

/// <summary>
/// Aggregates all project-scoped accessors for data sources, annotations, training, predictions, and augmentations.
/// </summary>
public class YoloEaseProject : RefreshableReactiveObject
{
    private static readonly Binder<YoloEaseProject> Binder = new();

    private readonly IYoloModelCachingService yoloModelCachingService;

    static YoloEaseProject()
    {
        Binder.Bind(x => x.StorageDirectory).To(x => x.RemoteProject.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.TrainingDataset.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.Assets.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.Predictions.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.AutoAnnotation.StorageDirectory);
        Binder.Bind(x => x.StorageDirectory).To(x => x.yoloModelCachingService.StorageDirectory);
    }

    public YoloEaseProject(
        AnnotationProjectAccessor annotationProjectAccessor,
        IYoloModelCachingService yoloModelCachingService,
        IFactory<DataSourcesProvider> fileSystemAssetsAccessor,
        IFactory<Yolo8PredictAccessor> predictAccessorFactory,
        IFactory<AutoAnnotationAccessor> autoAnnotationAccessorFactory,
        IFactory<AugmentationsAccessor, AnnotationsAccessor> augmentationsAccessorFactory,
        IFactory<LocalStorageAssetsAccessor, DataSourcesProvider> localStorageDatasetAccessorFactory,
        IFactory<Yolo8DatasetAccessor, IFileAssetsAccessor> trainingDatasetAccessorFactory,
        IFactory<AnnotationsAccessor, AnnotationProjectAccessor, IFileAssetsAccessor, Yolo8DatasetAccessor> annotationsAccessorFactory,
        IFactory<TrainingBatchAccessor, AnnotationProjectAccessor, IFileAssetsAccessor> batchAccessorFactory)
    {
        this.yoloModelCachingService = yoloModelCachingService;
        RemoteProject = annotationProjectAccessor.AddTo(Anchors);
        DataSources = fileSystemAssetsAccessor.Create().AddTo(Anchors);
        Predictions = predictAccessorFactory.Create().AddTo(Anchors);
        AutoAnnotation = autoAnnotationAccessorFactory.Create().AddTo(Anchors);
        Assets = localStorageDatasetAccessorFactory.Create(DataSources).AddTo(Anchors);
        TrainingDataset = trainingDatasetAccessorFactory.Create(Assets).AddTo(Anchors);
        TrainingBatch = batchAccessorFactory.Create(annotationProjectAccessor, Assets).AddTo(Anchors);
        Annotations = annotationsAccessorFactory.Create(annotationProjectAccessor, Assets, TrainingDataset).AddTo(Anchors);
        Augmentations = augmentationsAccessorFactory.Create(Annotations).AddTo(Anchors);
        Binder.Attach(this).AddTo(Anchors);
    }
    
    /// <summary>
    /// Gets or sets whether this instance is the empty sentinel project used when the shell is showing the start page.
    /// Empty projects are not backed by saved project state and must not refresh storage, annotations, training data, or remote state.
    /// </summary>
    public bool IsEmpty { get; set; }

    public DirectoryInfo StorageDirectory { get; set; }
    
    public AnnotationProjectAccessor RemoteProject { get; }
    
    public AugmentationsAccessor Augmentations { get; }
    
    public LocalStorageAssetsAccessor Assets { get; }
    
    public DataSourcesProvider DataSources { get; }
    
    public Yolo8DatasetAccessor TrainingDataset { get; }
    
    public Yolo8PredictAccessor Predictions { get; }

    public AutoAnnotationAccessor AutoAnnotation { get; }
    
    public TrainingBatchAccessor TrainingBatch { get; }
    
    public AnnotationsAccessor Annotations { get; }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        if (IsEmpty)
        {
            Log.Debug("Ignoring project refresh because the active project is empty");
            return;
        }

        await Assets.Refresh();
        await RemoteProject.Refresh();
        await Annotations.Refresh();
        await TrainingBatch.Refresh();
        await TrainingDataset.Refresh();
    }
}
