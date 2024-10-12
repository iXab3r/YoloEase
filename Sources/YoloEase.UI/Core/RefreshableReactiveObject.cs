using System.Diagnostics;
using System.Reactive.Subjects;
using AntDesign;
using JetBrains.Annotations;
using PoeShared.Blazor;
using PoeShared.Services;

namespace YoloEase.UI.Core;

public abstract class RefreshableReactiveObject : DisposableReactiveObjectWithLogger, IRefreshableComponent
{
    private static readonly Binder<RefreshableReactiveObject> Binder = new();

    private readonly SharedResourceLatch isBusyLatch;

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

    public async Task Refresh(IProgressReporter? progressReporter = default)
    {
        if (isBusyLatch.IsBusy)
        {
            throw new InvalidOperationException("Another refresh is already in progress");
        }
        using var isBusy = isBusyLatch.Rent();
        await RefreshInternal(progressReporter);
    }

    protected abstract Task RefreshInternal(IProgressReporter? progressReporter = default);

    protected IDisposable MarkAsBusy()
    {
        return isBusyLatch.Rent();
    }
}