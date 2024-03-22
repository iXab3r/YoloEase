using AntDesign;
using CvatApi;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.ProjectTree;

public class ProjectTreeViewModel : RefreshableReactiveObject
{
    private static readonly Binder<ProjectTreeViewModel> Binder = new();

    private readonly SourceList<ProjectTreeNode> nodesSource = new();
    private readonly ISourceCache<TaskTreeNode, int> taskNodeSource = new SourceCache<TaskTreeNode, int>(x => x.TaskId);
    private readonly ISourceCache<ProjectFileTreeNode, string> projectFileNodeSource = new SourceCache<ProjectFileTreeNode, string>(x => x.Id);
    private readonly ISourceCache<LocalFileTreeNode, string> localFileNodeSource = new SourceCache<LocalFileTreeNode, string>(x => x.Id);

    static ProjectTreeViewModel()
    {
    }

    public ProjectTreeViewModel()
    {
        PrepareTasksNode();
        PrepareFilesNode();
        PrepareLocalFilesNode();

        nodesSource.Connect()
            .ObserveOnCurrentDispatcher()
            .BindToCollection(out var nodes)
            .Subscribe()
            .AddTo(Anchors);
        Nodes = nodes;
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public async Task Refresh()
    {
        await Project.Refresh();
    }

    public YoloEaseProject Project { get; set; }

    public IReadOnlyObservableCollection<ProjectTreeNode> Nodes { get; }

    public TreeNode<ProjectTreeNode> SelectedNode { get; set; }

    public ProjectTreeNode SelectedItem { get; set; }

    private void PrepareTasksNode()
    {
        this.WhenAnyValue(x => x.Project)
            .Select(x => x != null ? x.RemoteProject.Tasks.AsObservableCache() : new IntermediateCache<TaskRead, int>())
            .Switch()
            .ObserveOnCurrentDispatcher()
            .ForEachChange(change =>
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    {
                        var added = change.Current;
                        var node = new TaskTreeNode(added.Id.Value);
                        node.Update(added);
                        taskNodeSource.AddOrUpdate(node);
                        break;
                    }
                    case ChangeReason.Update:
                    {
                        var updated = change.Current;
                        var node = taskNodeSource.Lookup(updated.Id.Value).Value;
                        node.Update(updated);
                        break;
                    }
                    case ChangeReason.Remove:
                    {
                        var removed = change.Current;
                        if (taskNodeSource.TryRemove(removed.Id.Value, out var node))
                        {
                            node.Dispose();
                        }
                    }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change), change, $"Unsupported reason: {change.Reason}, change: {change}");
                }
            })
            .Subscribe()
            .AddTo(Anchors);
        
        
        var tasksNode = ProjectTreeNode.FromNodes(taskNodeSource.AsObservableList()).AddTo(nodesSource);
        taskNodeSource.CountChanged.Subscribe(x => tasksNode.Name = x == 0 ? "Tasks" : $"Tasks ({x})").AddTo(Anchors);
        tasksNode.IsExpanded = false;
    }
    
    private void PrepareFilesNode()
    {
        this.WhenAnyValue(x => x.Project)
            .Select(x => x != null ? x.RemoteProject.ProjectFiles.AsObservableCache() : new IntermediateCache<TaskFileInfo, string>())
            .Switch()
            .ObserveOnCurrentDispatcher()
            .ForEachChange(change =>
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    {
                        var added = change.Current;
                        var node = new ProjectFileTreeNode(added);
                        node.Update(added);
                        projectFileNodeSource.AddOrUpdate(node);
                        break;
                    }
                    case ChangeReason.Update:
                    {
                        var updated = change.Current;
                        var tempNode = new ProjectFileTreeNode(updated);
                        var node = projectFileNodeSource.Lookup(tempNode.Id).Value;
                        node.Update(updated);
                        break;
                    }
                    case ChangeReason.Remove:
                    {
                        var removed = change.Current;
                        var tempNode = new ProjectFileTreeNode(removed);
                        if (projectFileNodeSource.TryRemove(tempNode.Id, out var node))
                        {
                            node.Dispose();
                        }
                    }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change), change, $"Unsupported reason: {change.Reason}, change: {change}");
                }
            })
            .Subscribe()
            .AddTo(Anchors);
        
        var filesNode = ProjectTreeNode.FromNodes(projectFileNodeSource.AsObservableList()).AddTo(nodesSource);
        projectFileNodeSource.CountChanged.Subscribe(x => filesNode.Name = x == 0 ? "Project Files" : $"Project Files ({x})").AddTo(Anchors);
        filesNode.IsExpanded = false;
    }
    
     private void PrepareLocalFilesNode()
    {
        this.WhenAnyValue(x => x.Project)
            .Select(x => x != null ? x.Assets.Files.AsObservableCache() : new IntermediateCache<FileInfo, string>())
            .Switch()
            .ObserveOnCurrentDispatcher()
            .ForEachChange(change =>
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    {
                        var added = change.Current;
                        var node = new LocalFileTreeNode(added);
                        node.Update(added);
                        localFileNodeSource.AddOrUpdate(node);
                        break;
                    }
                    case ChangeReason.Update:
                    {
                        var updated = change.Current;
                        var tempNode = new LocalFileTreeNode(updated);
                        var node = localFileNodeSource.Lookup(tempNode.Id).Value;
                        node.Update(updated);
                        break;
                    }
                    case ChangeReason.Remove:
                    {
                        var removed = change.Current;
                        var tempNode = new LocalFileTreeNode(removed);
                        if (localFileNodeSource.TryRemove(tempNode.Id, out var node))
                        {
                            node.Dispose();
                        }
                    }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change), change, $"Unsupported reason: {change.Reason}, change: {change}");
                }
            })
            .Subscribe()
            .AddTo(Anchors);
        
        var node = ProjectTreeNode.FromNodes(localFileNodeSource.AsObservableList()).AddTo(nodesSource);
        localFileNodeSource.CountChanged.Subscribe(x => node.Name = x == 0 ? "Local Files" : $"Local Files ({x})").AddTo(Anchors);
        node.IsExpanded = false;
    }
}