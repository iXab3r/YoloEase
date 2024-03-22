namespace YoloEase.UI.ProjectTree;

public class ProjectTreeNode : DisposableReactiveObject
{
    private readonly ISourceList<ProjectTreeNode> childrenSource = new SourceList<ProjectTreeNode>();

    public ProjectTreeNode()
    {
        childrenSource.Connect()
            .ObserveOnCurrentDispatcher()
            .BindToCollection(out var children)
            .Subscribe()
            .AddTo(Anchors);
        Children = children;
    }
    
    public bool IsExpanded { get; set; }

    public IReadOnlyObservableCollection<ProjectTreeNode> Children { get; }
    
    public string Name { get; set; }

    public static ProjectTreeNode FromNodes<T>(IObservableList<T> nodes) where T : ProjectTreeNode
    {
        var root = new ProjectTreeNode();
        nodes
            .Connect()
            .Transform(x => (ProjectTreeNode)x)
            .PopulateInto(root.childrenSource)
            .AddTo(root.Anchors);
        return root;
    }
}