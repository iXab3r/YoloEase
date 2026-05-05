using System.Linq;
using System.Reactive.Disposables;
using DynamicData;
using JetBrains.Annotations;
using PoeShared;
using PoeShared.Logging;
using PoeShared.Modularity;
using PoeShared.Scaffolding;
using PropertyBinder;
using ReactiveUI;

namespace YoloEase.UI.TaskAnnotation;

public sealed class ErrorsProvider<T> : DisposableReactiveObject, ICanSetErrors
{
    private static readonly IFluentLog Log = typeof(ErrorsProvider<T>).PrepareLogger();
    private static readonly Binder<ErrorsProvider<T>> Binder = new();
    private static readonly IComparer<ErrorInfo> Comparer = Comparer<ErrorInfo>.Create((left, right) => right.Timestamp.CompareTo(left.Timestamp));

    private readonly T owner;
    private readonly CircularSourceList<ErrorInfo> errorSource;
    private readonly SourceList<IHasErrors> externalErrorSources;

    public ErrorsProvider(T owner, int capacity)
    {
        this.owner = owner;
        externalErrorSources = new SourceList<IHasErrors>().AddTo(Anchors);
        errorSource = new CircularSourceList<ErrorInfo>(capacity).AddTo(Anchors);

        externalErrorSources
            .Connect()
            .Filter(x => x.Errors != null)
            .TransformMany(x => x.Errors)
            .ForEachItemChange(change =>
            {
                switch (change.Reason)
                {
                    case ListChangeReason.Add:
                        errorSource.Add(change.Current);
                        break;
                    case ListChangeReason.Remove:
                        errorSource.Remove(change.Current);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change), change.Reason, $"Change of type {change.Reason} is not supported by error merger, full change: {change}");
                }
            })
            .SubscribeToErrors(Log.HandleUiException)
            .AddTo(Anchors);

        Errors = errorSource
            .Connect()
            .Sort(Comparer)
            .AsObservableList()
            .AddTo(Anchors);

        Errors
            .Connect()
            .Subscribe(_ => LastError = Errors.Items.FirstOrDefault(), Log.HandleUiException)
            .AddTo(Anchors);

        Errors
            .CountChanged
            .Subscribe(x => HasErrors = x > 0, Log.HandleUiException)
            .AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public ErrorInfo? LastError { get; [UsedImplicitly] private set; }

    public bool HasErrors { get; [UsedImplicitly] private set; }

    public IObservableList<ErrorInfo> Errors { get; }

    public void Report(ErrorInfo errorInfo)
    {
        errorSource.Add(errorInfo);
    }

    public void Report(Exception exception)
    {
        Report(ErrorInfo.FromException(exception));
    }

    public IDisposable Report(IHasErrors source)
    {
        if (source == null)
        {
            return Disposable.Empty;
        }

        externalErrorSources.Add(source);
        return Disposable.Create(() => externalErrorSources.Remove(source));
    }

    public IDisposable ReportMany<TSource>(IObservableList<TSource> sources) where TSource : IHasErrors
    {
        if (sources == null)
        {
            return Disposable.Empty;
        }

        var addedItems = new List<IHasErrors>();
        return sources
            .Connect()
            .ForEachItemChange(change =>
            {
                externalErrorSources.Edit(list =>
                {
                    switch (change.Reason)
                    {
                        case ListChangeReason.Add:
                            list.Add(change.Current);
                            addedItems.Add(change.Current);
                            break;
                        case ListChangeReason.Remove:
                            list.Remove(change.Current);
                            addedItems.Remove(change.Current);
                            break;
                        case ListChangeReason.Replace:
                            list.Remove(change.Previous.Value);
                            list.Add(change.Current);
                            addedItems.Remove(change.Previous.Value);
                            addedItems.Add(change.Current);
                            break;
                        case ListChangeReason.Clear:
                            list.RemoveMany(addedItems);
                            addedItems.Clear();
                            break;
                        case ListChangeReason.Moved:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(change), change.Reason, $"Change of type {change.Reason} is not supported by ReportMany, full change: {change}");
                    }
                });
            })
            .Subscribe();
    }

    public IDisposable ReportMany<TSource, TKey>(IObservableCache<TSource, TKey> sources) where TSource : IHasErrors
    {
        if (sources == null)
        {
            return Disposable.Empty;
        }

        return sources
            .Connect()
            .ForEachChange(change =>
            {
                externalErrorSources.Edit(list =>
                {
                    switch (change.Reason)
                    {
                        case ChangeReason.Add:
                            list.Add(change.Current);
                            break;
                        case ChangeReason.Remove:
                            list.Remove(change.Current);
                            break;
                        case ChangeReason.Update:
                            list.Remove(change.Previous.Value);
                            list.Add(change.Current);
                            break;
                        case ChangeReason.Refresh:
                        case ChangeReason.Moved:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(change), change.Reason, $"Change of type {change.Reason} is not supported by ReportMany, full change: {change}");
                    }
                });
            })
            .Subscribe();
    }

    public void ReportSuccess()
    {
        LastError = null;
    }

    public void Clear()
    {
        foreach (var externalSourceItem in externalErrorSources.Items)
        {
            switch (externalSourceItem)
            {
                case ICanSetErrors canSetErrors:
                    canSetErrors.Clear();
                    break;
                case IHasErrorProvider hasErrorProvider:
                    hasErrorProvider.ErrorProvider.Clear();
                    break;
            }
        }

        errorSource.Clear();
        LastError = null;
    }

    protected override void FormatToString(ToStringBuilder builder)
    {
        base.FormatToString(builder);
        builder.AppendParameter(nameof(owner), owner);
    }
}
