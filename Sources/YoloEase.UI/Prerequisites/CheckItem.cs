using System.Diagnostics;
using System.Linq;
using System.Threading;
using PoeShared.Logging;
using PoeShared.Modularity;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Represents one prerequisite row, including dependency gates, diagnostics, and optional remediation.
/// </summary>
public sealed class CheckItem : DisposableReactiveObject, IHasError
{
    private static readonly IFluentLog Log = typeof(CheckItem).PrepareLogger();

    private readonly List<CheckAction> evaluationActions = new();
    private readonly List<CheckAction> remediationActions = new();
    private readonly List<CheckItem> dependencies = new();
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public string Id { get; } = $"CheckItem-{Guid.NewGuid()}";

    public string Name { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public bool IsRequired { get; init; } = true;

    public bool ShowEvaluateButton { get; init; } = true;

    public bool IncludeInBulkInstall { get; private set; } = true;

    public bool IsBusy { get; private set; }

    public bool IsExpanded { get; set; } = true;

    public string ProgressText { get; private set; }

    public string ProgressStatus { get; private set; }

    public string LastOutput { get; private set; }

    public bool? IsSatisfied { get; private set; }

    public ErrorInfo? LastError { get; private set; }

    public IReadOnlyList<CheckItem> Dependencies => dependencies;

    public bool HasRemediation => remediationActions.Count > 0;

    public string RemediationLabel => remediationActions.FirstOrDefault().Name ?? "Fix";

    public bool AreDependenciesSatisfied => dependencies.All(x => x.IsSatisfied == true);

    public bool IsBlocked => dependencies.Count > 0 && !AreDependenciesSatisfied;

    public string DependencyStatusText => AreDependenciesSatisfied
        ? string.Empty
        : $"Requires: {string.Join(", ", dependencies.Where(x => x.IsSatisfied != true).Select(x => x.Title))}";

    public bool CanRemediate => HasRemediation && AreDependenciesSatisfied;

    public bool CanEvaluate => evaluationActions.Count > 0 && AreDependenciesSatisfied;

    public CheckItem DependsOn(params CheckItem[] checks)
    {
        dependencies.AddRange(checks.Where(x => x != null));
        foreach (var dependency in dependencies)
        {
            dependency
                .WhenAnyValue(x => x.IsSatisfied)
                .Subscribe(_ => RaiseStatePropertiesChanged())
                .AddTo(Anchors);
        }

        RaiseStatePropertiesChanged();
        return this;
    }

    public CheckItem WithEvaluation(Func<CheckItem, CancellationToken, Task<bool?>> evaluateFunc)
    {
        evaluationActions.Add(new CheckAction("Check", evaluateFunc));
        return this;
    }

    public CheckItem WithRemediation(
        Func<CheckItem, CancellationToken, Task> remediateAction,
        string label = "Fix",
        bool includeInBulkInstall = true)
    {
        IncludeInBulkInstall = includeInBulkInstall;
        remediationActions.Add(new CheckAction(label, async (item, cancellationToken) =>
        {
            await remediateAction(item, cancellationToken);
            return true;
        }));
        return this;
    }

    public async Task<bool?> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return IsSatisfied;
        }

        var startedAt = Stopwatch.StartNew();
        var ensureMinimumDuration = true;
        try
        {
            SetBusy(true, "Checking...", "Checking prerequisite...");
            LastError = null;
            RaisePropertyChanged(nameof(LastError));

            if (!AreDependenciesSatisfied)
            {
                ensureMinimumDuration = false;
                AppendOutput(DependencyStatusText);
                IsSatisfied = false;
                IsExpanded = true;
                RaiseStatePropertiesChanged();
                RaisePropertyChanged(nameof(IsExpanded));
                return false;
            }

            bool? result = null;
            foreach (var evaluationAction in evaluationActions)
            {
                var actionResult = await evaluationAction.Func(this, cancellationToken);
                if (actionResult == null)
                {
                    continue;
                }

                result = result == null ? actionResult : result.Value && actionResult.Value;
            }

            IsSatisfied = result;
            RaiseStatePropertiesChanged();
            return result;
        }
        catch (Exception e)
        {
            LastError = ErrorInfo.FromException(e);
            AppendOutput($"Error: {e.Message}");
            IsSatisfied = false;
            IsExpanded = true;
            RaisePropertyChanged(nameof(LastError), nameof(IsExpanded));
            RaiseStatePropertiesChanged();
            return false;
        }
        finally
        {
            if (ensureMinimumDuration)
            {
                await EnsureMinimumDurationAsync(startedAt, cancellationToken);
            }
            SetBusy(false, null, null);
            operationLock.Release();
        }
    }

    public async Task RemediateAsync(CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        var startedAt = Stopwatch.StartNew();
        var ensureMinimumDuration = true;
        try
        {
            SetBusy(true, "Fixing...", "Fixing prerequisite...");
            LastError = null;
            RaisePropertyChanged(nameof(LastError));

            if (!AreDependenciesSatisfied)
            {
                ensureMinimumDuration = false;
                AppendOutput(DependencyStatusText);
                IsSatisfied = false;
                IsExpanded = true;
                RaiseStatePropertiesChanged();
                RaisePropertyChanged(nameof(IsExpanded));
                return;
            }

            foreach (var remediationAction in remediationActions)
            {
                await remediationAction.Func(this, cancellationToken);
            }
        }
        catch (Exception e)
        {
            LastError = ErrorInfo.FromException(e);
            AppendOutput($"Error: {e.Message}");
            IsSatisfied = false;
            IsExpanded = true;
            RaisePropertyChanged(nameof(LastError), nameof(IsExpanded));
            RaiseStatePropertiesChanged();
        }
        finally
        {
            if (ensureMinimumDuration)
            {
                await EnsureMinimumDurationAsync(startedAt, cancellationToken);
            }
            SetBusy(false, null, null);
            operationLock.Release();
        }
    }

    public void SetOutput(string output)
    {
        LastOutput = string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        RaisePropertyChanged(nameof(LastOutput));
    }

    public void AppendOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var trimmedOutput = output.Trim();
        Log.Info($"[{(string.IsNullOrWhiteSpace(Title) ? Name : Title)}] {trimmedOutput}");
        var nextOutput = string.IsNullOrWhiteSpace(LastOutput)
            ? trimmedOutput
            : $"{LastOutput}{Environment.NewLine}{trimmedOutput}";

        LastOutput = nextOutput;
        RaisePropertyChanged(nameof(LastOutput));
    }

    public void ClearOutput()
    {
        LastOutput = null;
        RaisePropertyChanged(nameof(LastOutput));
    }

    public void ClearDiagnostics()
    {
        LastOutput = null;
        LastError = null;
        ProgressStatus = null;
        RaisePropertyChanged(nameof(LastOutput), nameof(LastError), nameof(ProgressStatus));
    }

    public string StatusText => IsBlocked ? "Blocked" : IsSatisfied switch
    {
        true => IsRequired ? "Ready" : "Detected",
        false => IsRequired ? "Missing" : "Unavailable",
        _ => "Unknown"
    };

    public string StatusClass => IsBlocked ? "badge text-bg-secondary" : IsSatisfied switch
    {
        true => "badge text-bg-success",
        false when IsRequired => "badge text-bg-danger",
        false => "badge text-bg-warning",
        _ => "badge text-bg-secondary"
    };

    private void SetBusy(bool value, string progressText, string progressStatus)
    {
        IsBusy = value;
        ProgressText = progressText;
        ProgressStatus = progressStatus;
        RaisePropertyChanged(nameof(IsBusy), nameof(ProgressText), nameof(ProgressStatus));
    }

    private static async Task EnsureMinimumDurationAsync(Stopwatch startedAt, CancellationToken cancellationToken)
    {
        var remaining = TimeSpan.FromSeconds(1) - startedAt.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(remaining, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RaiseStatePropertiesChanged()
    {
        RaisePropertyChanged(
            nameof(IsSatisfied),
            nameof(StatusText),
            nameof(StatusClass),
            nameof(AreDependenciesSatisfied),
            nameof(IsBlocked),
            nameof(DependencyStatusText),
            nameof(CanEvaluate),
            nameof(CanRemediate),
            nameof(HasRemediation),
            nameof(RemediationLabel),
            nameof(IncludeInBulkInstall));
    }

    /// <summary>
    /// Stores one executable prerequisite action with its display phase name.
    /// </summary>
    private readonly record struct CheckAction(string Name, Func<CheckItem, CancellationToken, Task<bool?>> Func);
}
