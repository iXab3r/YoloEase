using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Humanizer;

namespace YoloEase.UI.TrainingTimeline;

public class TimeoutTimelineEntry : RunnableTimelineEntry
{
    public TimeoutTimelineEntry(TimeSpan timeout)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.Elapsed < Timeout && !cancellationToken.IsCancellationRequested)
            {
                var timeLeft = Timeout - sw.Elapsed;
                Text = $"Awaiting for {timeLeft.Humanize(culture: CultureInfo.InvariantCulture)}";
                ProgressPercent = (int) (timeLeft.TotalMilliseconds / Timeout.TotalMilliseconds * 100);
                await Task.Delay(1000, cancellationToken);
            }

            Text = $"Wait for {Timeout.Humanize(culture: CultureInfo.InvariantCulture)} completed";
        }
        catch (OperationCanceledException)
        {
            Text = $"Wait for {Timeout.Humanize(culture: CultureInfo.InvariantCulture)} cancelled after {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}";
        }
        finally
        {
            ProgressPercent = null;
        }
    }
}