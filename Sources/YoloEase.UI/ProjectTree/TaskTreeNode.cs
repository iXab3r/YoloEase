using CvatApi;

namespace YoloEase.UI.ProjectTree;

public sealed class TaskTreeNode : ProjectTreeNode
{
    public TaskTreeNode(int taskId)
    {
        TaskId = taskId;
    }
    
    public int TaskId { get; }
    public JobStatus? Status { get; set; }

    public void Update(TaskRead task)
    {
        if (TaskId != task.Id)
        {
            throw new ArgumentException($"Expected Task Id {task.Id}, got {task.Id}: {task}");
        }

        Name = task.Name;
        Status = task.Status;
    }
}