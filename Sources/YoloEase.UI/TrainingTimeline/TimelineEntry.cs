namespace YoloEase.UI.TrainingTimeline;

public abstract class TimelineEntry : DisposableReactiveObject
{
    public DateTime? Timestamp { get; init; }
    
    public string PrefixIcon { get; init; }
    
    public bool IsBusy { get; set; }
    
    public int? ProgressPercent { get; set; }
    
    public string Text { get; set; }

    public SourceListEx<FileInfo> Images { get; } = new();
}