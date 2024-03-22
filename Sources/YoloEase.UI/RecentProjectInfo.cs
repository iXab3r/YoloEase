namespace YoloEase.UI;

public sealed record RecentProjectInfo
{
    public string FilePath { get; set; }
    
    public DateTime AccessTime { get; set; }
}