using System.Linq;
using System.Threading;
using PoeShared.Blazor.Controls;
using PoeShared.Services;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.TrainingTimeline;

/// <summary>
/// Base class for timeline entries that can execute asynchronous work and expose progress state.
/// </summary>
public abstract class RunnableTimelineEntryBase : TimelineEntry
{
    private const int MaxOutputLines = 1000;

    private static readonly Binder<RunnableTimelineEntryBase> Binder = new();

    private readonly List<YoloCommandOutput> outputLog = new();
    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly SharedResourceLatch isBusyLatch;

    static RunnableTimelineEntryBase()
    {
        Binder.Bind(x => x.isBusyLatch.IsBusy).To(x => x.IsBusy);
    }

    protected RunnableTimelineEntryBase()
    {
        isBusyLatch = new SharedResourceLatch().AddTo(Anchors);
        cancellationTokenSource = new CancellationTokenSource();

        Binder.Attach(this).AddTo(Anchors);
    }

    public IReadOnlyList<YoloCommandOutput> OutputLog => outputLog;

    public int OutputLogCount { get; private set; }

    public bool HasOutputLog => OutputLogCount > 0;

    public string OutputLogPreview => outputLog.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.Text))?.Text ?? string.Empty;

    public string OutputLogText => string.Join(Environment.NewLine, outputLog.Select(FormatOutputLine));

    public async Task Cancel()
    {
        cancellationTokenSource.Cancel();
    }

    protected CancellationTokenSource CreateCombinedCancellationTokenSource(CancellationToken cancellationToken)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
    }

    protected IDisposable RentBusy()
    {
        return isBusyLatch.Rent();
    }

    protected void AppendOutputLog(YoloCommandOutput output)
    {
        if (string.IsNullOrWhiteSpace(output.Text))
        {
            return;
        }

        outputLog.Add(output);
        while (outputLog.Count > MaxOutputLines)
        {
            outputLog.RemoveAt(0);
        }

        OutputLogCount = outputLog.Count;
        RaisePropertyChanged(nameof(OutputLogCount));
        RaisePropertyChanged(nameof(HasOutputLog));
        RaisePropertyChanged(nameof(OutputLogPreview));
        RaisePropertyChanged(nameof(OutputLogText));
    }

    private static string FormatOutputLine(YoloCommandOutput output)
    {
        var prefix = output.Kind switch
        {
            YoloCommandOutputKind.Info => "info",
            YoloCommandOutputKind.Output => "out ",
            YoloCommandOutputKind.Error => "err ",
            _ => "log "
        };
        return $"{output.Timestamp:HH:mm:ss} {prefix}> {output.Text}";
    }
}

public abstract class RunnableTimelineEntry : RunnableTimelineEntryBase
{
    public async Task Run(CancellationToken cancellationToken)
    {
        using var isBusy = RentBusy();
        using var combinedCancellationToken = CreateCombinedCancellationTokenSource(cancellationToken);

        await RunInternal(combinedCancellationToken.Token);
    }
    
    protected abstract Task RunInternal(CancellationToken cancellationToken);
}

/// <summary>
/// Base class for runnable timeline entries that produce a typed result.
/// </summary>
public abstract class RunnableTimelineEntry<T> : RunnableTimelineEntryBase
{
    public async Task<T> Run(CancellationToken cancellationToken)
    {
        using var isBusy = RentBusy();
        using var combinedCancellationToken = CreateCombinedCancellationTokenSource(cancellationToken);

        return await Task.Run(() => RunInternal(combinedCancellationToken.Token), combinedCancellationToken.Token);
    }
    
    protected abstract Task<T> RunInternal(CancellationToken cancellationToken);
}
