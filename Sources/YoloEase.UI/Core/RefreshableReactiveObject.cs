using System.Diagnostics;
using System.Reactive.Subjects;
using System.Threading;
using AntDesign;
using JetBrains.Annotations;
using PoeShared.Blazor;
using PoeShared.Services;

namespace YoloEase.UI.Core;

/// <summary>
/// Base class for view models that expose single-flight refresh behavior and busy state.
/// </summary>
public abstract class RefreshableReactiveObject : DisposableReactiveObjectWithLogger, IRefreshableComponent
{
    private static readonly Binder<RefreshableReactiveObject> Binder = new();
    private static readonly AsyncLocal<RefreshableReactiveObject?> ActiveRefresh = new();

    private readonly object refreshGate = new();
    private readonly SharedResourceLatch isBusyLatch;
    private Task? currentRefreshTask;
    private TaskCompletionSource? currentRefreshCompletion;
    private bool refreshRequested;
    private long refreshGeneration;

    static RefreshableReactiveObject()
    {
        Binder.Bind(x => x.isBusyLatch.IsBusy).To(x => x.IsBusy);
    }

    protected RefreshableReactiveObject()
    {
        isBusyLatch = new SharedResourceLatch().AddTo(Anchors);
        Binder.Attach(this).AddTo(Anchors);
    }

    public bool IsBusy { get; [UsedImplicitly] private set; }

    public ISubject<object> WhenRefresh { get; } = new Subject<object>();

    public ISubject<NotificationConfig> WhenNotified { get; } = new Subject<NotificationConfig>();

    public Task Refresh(IProgressReporter? progressReporter = default)
    {
        Task refreshTask;
        lock (refreshGate)
        {
            if (currentRefreshTask != null)
            {
                refreshRequested = true;
                if (Log.IsDebugEnabled)
                {
                    Log.Debug($"Refresh requested while generation {refreshGeneration} is active; coalescing one follow-up refresh");
                }

                if (ReferenceEquals(ActiveRefresh.Value, this))
                {
                    return Task.CompletedTask;
                }

                return currentRefreshTask;
            }

            currentRefreshCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            currentRefreshTask = currentRefreshCompletion.Task;
            refreshTask = currentRefreshTask;
        }

        _ = RunRefreshLoop(progressReporter);
        return refreshTask;
    }

    protected abstract Task RefreshInternal(IProgressReporter? progressReporter = default);

    protected IDisposable MarkAsBusy()
    {
        return isBusyLatch.Rent();
    }

    private async Task RunRefreshLoop(IProgressReporter? progressReporter)
    {
        Exception? error = null;
        TaskCompletionSource? completion = null;
        try
        {
            using var isBusy = isBusyLatch.Rent();
            while (true)
            {
                var generation = BeginRefreshPass();
                if (Log.IsDebugEnabled)
                {
                    Log.Debug($"Starting refresh generation {generation}");
                }

                var previousActiveRefresh = ActiveRefresh.Value;
                ActiveRefresh.Value = this;
                try
                {
                    await RefreshInternal(progressReporter);
                }
                finally
                {
                    ActiveRefresh.Value = previousActiveRefresh;
                }

                lock (refreshGate)
                {
                    if (refreshRequested)
                    {
                        if (Log.IsDebugEnabled)
                        {
                            Log.Debug($"Refresh generation {generation} completed with pending refresh request; running one coalesced follow-up");
                        }

                        continue;
                    }

                    currentRefreshTask = null;
                    completion = currentRefreshCompletion;
                    currentRefreshCompletion = null;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            error = e;
            lock (refreshGate)
            {
                currentRefreshTask = null;
                refreshRequested = false;
                completion = currentRefreshCompletion;
                currentRefreshCompletion = null;
            }
        }
        finally
        {
            if (error == null)
            {
                completion?.TrySetResult();
            }
            else
            {
                completion?.TrySetException(error);
            }
        }
    }

    private long BeginRefreshPass()
    {
        lock (refreshGate)
        {
            refreshRequested = false;
            return ++refreshGeneration;
        }
    }
}
