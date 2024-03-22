using System.Reactive.Disposables;
using System.Threading;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.TrainingTimeline;

public class UpdateYoloTimelineEntry : RunnableTimelineEntry
{
    private readonly Yolo8CliWrapper yolo8CliWrapper;

    public UpdateYoloTimelineEntry(
        TimelineController timelineController,
        Yolo8CliWrapper yolo8CliWrapper)
    {
        TimelineController = timelineController;
        CanRequestUpdate = !timelineController.PerformUpdateOnNextCycle;
        this.yolo8CliWrapper = yolo8CliWrapper;
    }

    public TimelineController TimelineController { get; }
    
    public string UpdateText { get; set; }
    
    public bool CanRequestUpdate { get; }
    
    public bool UpdateRequested { get; set; }
    
    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        if (!TimelineController.PerformUpdateOnNextCycle)
        {
            Text = "Installed version of Yolo is obsolete, you have to update it";
        }
        else
        {
            Text = "Updating Yolo via PIP";
            await yolo8CliWrapper.UpdateYolo(cancellationToken);
            Text = "Updated Yolo to the latest version";
        }
    }
}