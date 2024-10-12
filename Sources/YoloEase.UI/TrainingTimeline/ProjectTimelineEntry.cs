using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading;
using YoloEase.UI.Core;

namespace YoloEase.UI.TrainingTimeline;

public class ProjectTimelineEntry : RunnableTimelineEntry
{
    public YoloEaseProject Project { get; }

    public ProjectTimelineEntry(YoloEaseProject project)
    {
        Project = project;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        var sw = Stopwatch.StartNew();
        ProgressPercent = 0;
        Text = "Refreshing local/remote assets";
        using var progressTracker = new ComplexProgressTracker();
        using var progressTrackerAnchor = progressTracker.WhenAnyValue(x => x.ProgressPercent)
            .Subscribe(x => ProgressPercent = x);

        {
            var reporter = progressTracker.GetOrAdd("Data sources");
            using var reporterAnchor = reporter.WhenAnyValue(x => x.ProgressPercent)
                .Subscribe(x => Text = $"Refreshing data sources: {x:F0}%");
            await Project.DataSources.Refresh(reporter);
            cancellationToken.ThrowIfCancellationRequested();
            AppendTextLine($"Directories: {Project.DataSources.InputDirectories.Count}]");
        }

        {
            var reporter = progressTracker.GetOrAdd("Local assets");
            using var reporterAnchor = reporter.WhenAnyValue(x => x.ProgressPercent)
                .Subscribe(x => Text = $"Refreshing local assets: {x:F0}%");
            await Project.Assets.Refresh(reporter);
            cancellationToken.ThrowIfCancellationRequested();
            AppendTextLine($"Input files: {Project.Assets.Files.Count}, dirs: {Project.Assets.InputDirectories.Count}");
        }
        
        {
            Text = "Preparing training batch";
            await Project.TrainingBatch.Refresh();
            cancellationToken.ThrowIfCancellationRequested();
            AppendTextLine($"Prepared training batch, size: {Project.TrainingBatch.BatchFiles.Count}");
        }
        
        {
            Text = "Refreshing remote assets";
            await Project.RemoteProject.Refresh();
            cancellationToken.ThrowIfCancellationRequested();
            AppendTextLine($"Remote project updated");
        }
        
        {
            Text = "Refreshing remote annotations";
            await Project.Annotations.Refresh();
            cancellationToken.ThrowIfCancellationRequested();
            AppendTextLine($"Annotations: {Project.Annotations.Annotations.Count}");
        }

        Text = $"Refreshed local/remote assets in {sw.Elapsed}";
    }
}