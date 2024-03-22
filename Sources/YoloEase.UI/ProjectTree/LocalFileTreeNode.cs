namespace YoloEase.UI.ProjectTree;

public sealed class LocalFileTreeNode : ProjectTreeNode
{
    public LocalFileTreeNode(FileInfo file)
    {
        Id = $"{file.FullName}";
        Update(file);
    }
    
    public string Id { get; }

    public void Update(FileInfo file)
    {
        Name = file.FullName;
    }
}