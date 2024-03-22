using System.Linq;
using CvatApi;
using JetBrains.Annotations;

namespace YoloEase.UI.Core;

public class TrainingBatchAccessor : RefreshableReactiveObject
{
    private static readonly Binder<TrainingBatchAccessor> Binder = new();

    private readonly SourceCacheEx<FileInfo, string> batchFileSources = new(x => x.FullName);
    private readonly ICvatClient cvatClient;

    private readonly SourceCacheEx<FileInfo, string> unannotatedFilesSource = new(x => x.FullName);

    static TrainingBatchAccessor()
    {
        Binder.Bind(x => (int) Math.Round(x.BatchPercentage / 100f * x.UnannotatedFiles.Count)).To(x => x.BatchSize);
    }

    public TrainingBatchAccessor(
        CvatProjectAccessor project,
        IFileAssetsAccessor assets,
        ICvatClient cvatClient)
    {
        Project = project;
        Assets = assets;
        this.cvatClient = cvatClient;

        UnannotatedFiles = unannotatedFilesSource.ToSourceListEx().AddTo(Anchors);
        BatchFiles = batchFileSources.ToSourceListEx().AddTo(Anchors);
        
        UnannotatedTasks = Project.Tasks
            .Connect()
            .Filter(x => x.Status != JobStatus.Completed)
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

    public CvatProjectAccessor Project { get; }
    public IFileAssetsAccessor Assets { get; }

    public int MinBatchPercentage { get; private set; } = 1;
    public int MaxBatchPercentage { get; private set; } = 100;
    public int BatchPercentage { get; set; } = 5;

    public int BatchSize { get; [UsedImplicitly] private set; }
    
    public IObservableListEx<TaskRead> Tasks { get; }

    public IObservableListEx<FileInfo> UnannotatedFiles { get; }
    
    public IObservableListEx<TaskRead> UnannotatedTasks { get; }

    public IObservableListEx<FileInfo> BatchFiles { get; }

    public async Task<TaskRead> CreateNextTask()
    {
        var nextBatch = batchFileSources.Items.ToArray();
        var task = await Project.CreateTask(nextBatch);
        await Project.Refresh();
        await Refresh();
        await Project.NavigateToTask(task.Id.Value);
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

    public async Task Refresh()
    {
        if (isBusyLatch.IsBusy)
        {
            throw new InvalidOperationException("Another refresh is already in progress");
        }
        using var isBusy = isBusyLatch.Rent();

        var projectFiles = Project.ProjectFiles.Items.ToDictionary(x => x.FileName);
        var localFiles = Assets.Files.Items.ToDictionary(x => x.Name);

        var unannotatedFiles = localFiles.Where(x => !projectFiles.ContainsKey(x.Key)).Select(x => x.Value).ToArray();
        unannotatedFilesSource.EditDiff(unannotatedFiles);
    }
}