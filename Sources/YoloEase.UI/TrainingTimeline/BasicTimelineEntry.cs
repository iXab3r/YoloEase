using PoeShared.Blazor.Controls;

namespace YoloEase.UI.TrainingTimeline;

public abstract class TrainerTimelineEntryBase : TimelineEntry
{
    public string PrefixIcon { get; init; }
}