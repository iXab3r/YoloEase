namespace YoloEase.UI;

/// <summary>
/// Records a project path and its last access time for the recent-projects list.
/// </summary>
public sealed record RecentProjectInfo
{
    public string FilePath { get; set; }
    
    public DateTime AccessTime { get; set; }
}
