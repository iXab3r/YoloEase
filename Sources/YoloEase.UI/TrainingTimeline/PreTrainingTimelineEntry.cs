using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading;
using PoeShared.Logging;
using PoeShared.Native;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.TrainingTimeline;

public class PreTrainingTimelineEntry : RunnableTimelineEntry
{
    private static readonly IFluentLog Log = typeof(PreTrainingTimelineEntry).PrepareLogger();

    private readonly IWindowHandleProvider windowHandleProvider;
    private readonly TimelineController timelineController;
    private readonly Yolo8DatasetAccessor yolo8DatasetAccessor;

    public PreTrainingTimelineEntry(
        IWindowHandleProvider windowHandleProvider,
        TimelineController timelineController,
        Yolo8DatasetAccessor yolo8DatasetAccessor,
        DatasetInfo datasetInfo)
    {
        DatasetInfo = datasetInfo;
        this.windowHandleProvider = windowHandleProvider;
        this.timelineController = timelineController;
        this.yolo8DatasetAccessor = yolo8DatasetAccessor;
    }

    public DatasetInfo DatasetInfo { get; }

    public Yolo8ChecksResult ChecksResult { get; private set; }
    
    public int TerminatedStaleProcessCount { get; private set; }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        Text = "Running pre-training checks...";

        var killedProcesses = KillStaleYoloProcesses(DatasetInfo);
        TerminatedStaleProcessCount = killedProcesses.Count;

        ChecksResult = await yolo8DatasetAccessor.RunChecks(cancellationToken);
        Text = "Checks completed";
    }

    private static IReadOnlyList<Process> KillStaleYoloProcesses(DatasetInfo datasetInfo)
    {
        var matchingProcesses = new List<Process>();
        var allProcesses = Process.GetProcessesByName("yolo");

        var storageDirectory = datasetInfo.IndexFile.Directory!.Parent!.FullName;

        foreach (var process in allProcesses)
        {
          
            try
            {
                if (process.MainModule == null)
                {
                    continue;
                }
                
                // Process.MainModule can throw an exception if the process has exited or if you do not have access rights.
                var commandLine = UnsafeNative.GetCommandLine(process.Id) ?? string.Empty;

                if (!commandLine.Contains(storageDirectory))
                {
                    continue;
                }

                try
                {
                    Log.Warn($"Stale process detected: {new {process.Id, commandLine}}");
                    process.Kill(entireProcessTree: true);
                    matchingProcesses.Add(process);
                    Log.Warn($"Stale process terminated successfully: {new {process.Id, commandLine}}");
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to terminated stale process: {new {process.Id, commandLine}}", e);
                    throw;
                }
            }
            catch
            {
                // Handle exceptions or ignore the process if there is an access problem.
            }
        }

        return matchingProcesses;
    }
}