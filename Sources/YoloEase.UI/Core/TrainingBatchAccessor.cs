using System.Linq;
using JetBrains.Annotations;

namespace YoloEase.UI.Core;

/// <summary>
/// Tracks the batch percentage used to select training tasks.
/// </summary>
public class TrainingBatchAccessor : RefreshableReactiveObject
{
    private static readonly Binder<TrainingBatchAccessor> Binder = new();

    private readonly SourceCacheEx<FileInfo, string> batchFileSources = new(x => x.FullName);
    private readonly SourceCacheEx<FileInfo, string> unannotatedFilesSource = new(x => x.FullName);

    static TrainingBatchAccessor()
    {
        Binder.Bind(x => (int) Math.Round(x.BatchPercentage / 100f * x.UnannotatedFiles.Count)).To(x => x.BatchSize);
    }

    public TrainingBatchAccessor(
        AnnotationProjectAccessor project,
        IFileAssetsAccessor assets)
    {
        Project = project;
        Assets = assets;

        UnannotatedFiles = unannotatedFilesSource.ToSourceListEx().AddTo(Anchors);
        BatchFiles = batchFileSources.ToSourceListEx().AddTo(Anchors);
        
        UnannotatedTasks = Project.Tasks
            .Connect()
            .Filter(x => x.Status != AnnotationTaskStatus.Completed)
            .RemoveKey()
            .ToSourceListEx()
            .AddTo(Anchors);
        
        Tasks = Project.Tasks
            .Connect()
            .RemoveKey()
            .ToSourceListEx()
            .AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public AnnotationProjectAccessor Project { get; }
    public IFileAssetsAccessor Assets { get; }

    public int MinBatchPercentage { get; private set; } = 1;
    public int MaxBatchPercentage { get; private set; } = 100;
    public int BatchPercentage { get; set; } = 5;

    public int BatchSize { get; [UsedImplicitly] private set; }
    
    public IObservableListEx<AnnotationTaskInfo> Tasks { get; }

    public IObservableListEx<FileInfo> UnannotatedFiles { get; }
    
    public IObservableListEx<AnnotationTaskInfo> UnannotatedTasks { get; }

    public IObservableListEx<FileInfo> BatchFiles { get; }
    
    public async Task<AnnotationTaskInfo> CreateNextTask()
    {
        var nextBatch = batchFileSources.Items.ToArray();
        var task = await Project.CreateTask(nextBatch);
        await Project.Refresh();
        await Refresh();
        return task;
    }

    public async Task<FileInfo[]> PrepareNextBatchFiles(FileInfo[] nextBatch)
    {
        batchFileSources.EditDiff(nextBatch);
        return batchFileSources.Items.ToArray();
    }
    
    public Task<FileInfo[]> PrepareNextBatchFiles()
    {
        var nextBatch = unannotatedFilesSource.Items.Randomize().Take(BatchSize).ToArray();
        return PrepareNextBatchFiles(nextBatch);
    }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        var projectFiles = Project.ProjectFiles.Items.ToDictionary(x => x.FileName);
        var localFiles = Assets.Files.Items.ToDictionary(x => x.Name);

        var unannotatedFiles = localFiles.Where(x => !projectFiles.ContainsKey(x.Key)).Select(x => x.Value).ToArray();
        unannotatedFilesSource.EditDiff(unannotatedFiles);
    }
}
