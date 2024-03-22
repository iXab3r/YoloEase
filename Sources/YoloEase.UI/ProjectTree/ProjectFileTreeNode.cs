using YoloEase.UI.Dto;

namespace YoloEase.UI.ProjectTree;

public sealed class ProjectFileTreeNode : ProjectTreeNode
{
    public ProjectFileTreeNode(TaskFileInfo file)
    {
        Id = $"{file.TaskId} {file.FileName}";
        Update(file);
    }
    
    public string Id { get; }

    public void Update(TaskFileInfo file)
    {
        Name = file.FileName;
    }
}