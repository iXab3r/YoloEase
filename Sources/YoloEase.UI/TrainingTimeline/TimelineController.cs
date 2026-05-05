using PoeShared.Blazor.Controls;

namespace YoloEase.UI.TrainingTimeline;

/// <summary>
/// Coordinates sequential execution of the training timeline entries.
/// </summary>
public sealed class TimelineController 
{
    private readonly CircularSourceList<TimelineEntry> itemsSource;
    
    public TimelineController(CircularSourceList<TimelineEntry> itemsSource)
    {
        this.itemsSource = itemsSource;
    }
    
    public bool PerformUpdateOnNextCycle { get; set; }

    public void Cancel()
    {
        
    }

    public void Add(TimelineEntry entry)
    {
        itemsSource.Add(entry);
    }
}
