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
        FileSystemAssetsAccessor fileSystemAssetsAccessor,
        IFactory<LocalStorageAssetsAccessor, IFileAssetsAccessor> localStorageDatasetAccessorFactory,
        IFactory<Yolo8DatasetAccessor, IFileAssetsAccessor> trainingDatasetAccessorFactory,
        IFactory<Yolo8PredictAccessor, IFileAssetsAccessor> predictAccessorFactory,
        IFactory<AnnotationsAccessor,  CvatProjectAccessor, IFileAssetsAccessor, Yolo8DatasetAccessor> annotationsAccessorFactory,
        IFactory<TrainingBatchAccessor, CvatProjectAccessor, IFileAssetsAccessor> batchAccessorFactory)
    {
        this.yoloModelCachingService = yoloModelCachingService;
        RemoteProject = cvatProjectAccessor.AddTo(Anchors);
        FileSystemAssets = fileSystemAssetsAccessor.AddTo(Anchors);
        Assets = localStorageDatasetAccessorFactory.Create(FileSystemAssets).AddTo(Anchors);
        Predictions = predictAccessorFactory.Create(FileSystemAssets).AddTo(Anchors);
        TrainingDataset = trainingDatasetAccessorFactory.Create(Assets).AddTo(Anchors);
        TrainingBatch = batchAccessorFactory.Create(cvatProjectAccessor, Assets).AddTo(Anchors);
        Annotations = annotationsAccessorFactory.Create(cvatProjectAccessor, Assets, TrainingDataset).AddTo(Anchors);
        Binder.Attach(this).AddTo(Anchors);
    }
    
    public DirectoryInfo StorageDirectory { get; set; }
    
    public CvatProjectAccessor RemoteProject { get; }
    
    public LocalStorageAssetsAccessor Assets { get; }
    
    public FileSystemAssetsAccessor FileSystemAssets { get; }
    
    public Yolo8DatasetAccessor TrainingDataset { get; }
    
    public Yolo8PredictAccessor Predictions { get; }
    
    public TrainingBatchAccessor TrainingBatch { get; }
    
    public AnnotationsAccessor Annotations { get; }

    public async Task Refresh()
    {
        if (isBusyLatch.IsBusy)
        {
            throw new InvalidOperationException("Another refresh is already in progress");
        }
        using var isBusy = isBusyLatch.Rent();
        
        await Assets.Refresh();
        await RemoteProject.Refresh();
        await Annotations.Refresh();
        await TrainingBatch.Refresh();
        await TrainingDataset.Refresh();
    }
}