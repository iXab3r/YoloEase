namespace YoloEase.UI.TrainingTimeline;

public class ErrorTimelineEntry : TimelineEntry
{
    public Exception Exception { get; }

    public ErrorTimelineEntry(Exception exception)
    {
        Exception = exception;
        Text = "Error occurred";
    }
}