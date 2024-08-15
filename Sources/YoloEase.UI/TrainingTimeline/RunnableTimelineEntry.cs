using System.Threading;
using PoeShared.Services;

namespace YoloEase.UI.TrainingTimeline;

public abstract class RunnableTimelineEntry : TrainerTimelineEntryBase
{
    private static readonly Binder<RunnableTimelineEntry> Binder = new();

    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly SharedResourceLatch isBusyLatch;
    
    static RunnableTimelineEntry()
    {
        Binder.Bind(x => x.isBusyLatch.IsBusy).To(x => x.IsBusy);
    }

    protected RunnableTimelineEntry()
    {
        isBusyLatch = new SharedResourceLatch().AddTo(Anchors);
        cancellationTokenSource = new CancellationTokenSource().AddTo(Anchors);
        
        Binder.Attach(this).AddTo(Anchors);
    }
    
    public async Task Cancel()
    {
        cancellationTokenSource.Cancel();
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        using var isBusy = isBusyLatch.Rent();
        using var combinedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);

        await RunInternal(combinedCancellationToken.Token);
    }
    
    protected abstract Task RunInternal(CancellationToken cancellationToken);
}

public abstract class RunnableTimelineEntry<T> : TrainerTimelineEntryBase
{
    private static readonly Binder<RunnableTimelineEntry<T>> Binder = new();

    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly SharedResourceLatch isBusyLatch;
    
    static RunnableTimelineEntry()
    {
        Binder.Bind(x => x.isBusyLatch.IsBusy).To(x => x.IsBusy);
    }

    protected RunnableTimelineEntry()
    {
        isBusyLatch = new SharedResourceLatch().AddTo(Anchors);
        cancellationTokenSource = new CancellationTokenSource().AddTo(Anchors);
        
        Binder.Attach(this).AddTo(Anchors);
    }
    
    public async Task Cancel()
    {
        cancellationTokenSource.Cancel();
    }

    public async Task<T> Run(CancellationToken cancellationToken)
    {
        using var isBusy = isBusyLatch.Rent();
        using var combinedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);

        return await Task.Run(() => RunInternal(combinedCancellationToken.Token), combinedCancellationToken.Token);
    }
    
    protected abstract Task<T> RunInternal(CancellationToken cancellationToken);
}