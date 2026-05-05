using System.Linq;
using System.Text;
using System.Threading;
using PoeShared.Common;
using PoeShared.Modularity;
using YoloEase.UI.Core;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Drives the prerequisites UI, startup-check preference, operation progress, and diagnostic text.
/// </summary>
public sealed class PrerequisitesViewModel : RefreshableReactiveObject, ICanBeSelected
{
    private readonly IConfigProvider<YoloEaseApplicationConfig> configProvider;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private int startupCheckRequested;

    public PrerequisitesViewModel(
        IConfigProvider<YoloEaseApplicationConfig> configProvider,
        IPrerequisitesToolchain toolchain,
        PrerequisitesSuiteFactory suiteFactory)
        : this(configProvider, toolchain, suiteFactory.Create())
    {
    }

    public PrerequisitesViewModel(
        IConfigProvider<YoloEaseApplicationConfig> configProvider,
        IPrerequisitesToolchain toolchain,
        CheckSuite suite)
    {
        this.configProvider = configProvider;
        Toolchain = toolchain;
        Suite = suite.AddTo(Anchors);
        CheckPrerequisitesAtStartup = configProvider.ActualConfig.CheckPrerequisitesAtStartup;

        configProvider.WhenChanged
            .Subscribe(x =>
            {
                CheckPrerequisitesAtStartup = x.CheckPrerequisitesAtStartup;
                RaisePropertyChanged(nameof(CheckPrerequisitesAtStartup));
            })
            .AddTo(Anchors);

        Suite.Checks
            .Connect()
            .AutoRefresh(x => x.IsSatisfied)
            .AutoRefresh(x => x.IsBusy)
            .AutoRefresh(x => x.IsBlocked)
            .AutoRefresh(x => x.LastOutput)
            .AutoRefresh(x => x.LastError)
            .AutoRefresh(x => x.ProgressStatus)
            .Subscribe(_ =>
            {
                RefreshStatus();
                RaisePropertyChanged(nameof(HasAnyLogs));
            })
            .AddTo(Anchors);
    }

    public CheckSuite Suite { get; }

    public IPrerequisitesToolchain Toolchain { get; }

    public bool CheckPrerequisitesAtStartup { get; private set; }

    public bool HasEverEvaluated { get; private set; }

    public bool HasMissingRequired { get; private set; }

    public int MissingRequiredCount { get; private set; }

    public bool IsOperationBusy { get; private set; }

    public bool IsSelected { get; set; }

    public string SummaryText { get; private set; } = "Not checked yet";

    public string OperationTitleText { get; private set; } = "Prerequisites";

    public string OperationStatusText { get; private set; } = "Idle";

    public double OperationProgressPercent { get; private set; }

    public bool OperationFailed { get; private set; }

    public bool HasAnyLogs => Suite.Checks.Items.Any(x =>
        !string.IsNullOrWhiteSpace(x.LastOutput) ||
        x.LastError != null ||
        !string.IsNullOrWhiteSpace(x.ProgressStatus));

    public bool ShowProgressStrip => IsOperationBusy || OperationProgressPercent > 0 || OperationFailed;

    public string OperationProgressVisibilityClass => ShowProgressStrip ? string.Empty : "is-hidden";

    public string OperationProgressText => $"{OperationProgressPercent:F0}%";

    public string OperationProgressStyle => $"width: {OperationProgressPercent:F1}%;";

    public string OperationProgressClass => OperationFailed
        ? "is-failed"
        : IsOperationBusy
            ? "is-active"
            : OperationProgressPercent >= 100
                ? "is-complete"
                : string.Empty;

    public string HeaderStatusText => HasEverEvaluated
        ? MissingRequiredCount == 0
            ? "All ready"
            : $"{MissingRequiredCount} missing"
        : "Not checked";

    public string HeaderStatusClass => HasEverEvaluated
        ? MissingRequiredCount == 0
            ? "text-bg-success"
            : "text-bg-danger"
        : "text-bg-secondary";

    public string ToolsRootPath => Toolchain.ToolsRoot.FullName;

    public async Task RequestStartupCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!CheckPrerequisitesAtStartup)
        {
            return;
        }

        if (Interlocked.Exchange(ref startupCheckRequested, 1) == 1)
        {
            return;
        }

        await EvaluateAllAsync(cancellationToken);
    }

    public async Task NotifyTabActivated(CancellationToken cancellationToken = default)
    {
        if (!HasEverEvaluated)
        {
            await EvaluateAllAsync(cancellationToken);
        }
    }

    public async Task EvaluateAllAsync(CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            SetOperationStatus("Prerequisites are already being checked or installed.");
            return;
        }

        try
        {
            BeginOperation("Checking prerequisites", "Starting checks...");
            SetBusy(true, "Checking prerequisites...");
            await RunOffUiThreadAsync(
                () => Suite.EvaluateAllAsync(UpdateOperationProgress, cancellationToken),
                cancellationToken);
            HasEverEvaluated = true;
            RefreshStatus();
            CompleteOperation(true, SummaryText);
        }
        catch (Exception e)
        {
            SummaryText = $"Check failed: {e.Message}";
            HasMissingRequired = true;
            CompleteOperation(false, SummaryText);
            RaiseHeaderPropertiesChanged();
        }
        finally
        {
            SetBusy(false, SummaryText);
            operationLock.Release();
        }
    }

    public async Task EvaluateCheckAsync(CheckItem check, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            SetOperationStatus("Prerequisites are already being checked or installed.");
            return;
        }

        try
        {
            BeginOperation($"Checking {check.Title}", "Starting check...");
            SetBusy(true, $"Checking {check.Title}...");
            await RunOffUiThreadAsync(async () =>
            {
                UpdateOperationProgress(new CheckSuiteProgress(check, "Checking", 0, 1));
                await check.EvaluateAsync(cancellationToken);
                UpdateOperationProgress(new CheckSuiteProgress(check, "Checked", 1, 1));
            }, cancellationToken);
            HasEverEvaluated = true;
            RefreshStatus();
            CompleteOperation(check.IsSatisfied == true || !check.IsRequired, SummaryText);
        }
        catch (Exception e)
        {
            SummaryText = $"Check failed: {e.Message}";
            HasMissingRequired = true;
            CompleteOperation(false, SummaryText);
            RaiseHeaderPropertiesChanged();
        }
        finally
        {
            SetBusy(false, SummaryText);
            operationLock.Release();
        }
    }

    public async Task RemediateCheckAsync(CheckItem check, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            SetOperationStatus("Prerequisites are already being checked or installed.");
            return;
        }

        try
        {
            BeginOperation($"{check.RemediationLabel}: {check.Title}", "Starting fix...");
            SetBusy(true, $"Fixing {check.Title}...");
            await RunOffUiThreadAsync(async () =>
            {
                UpdateOperationProgress(new CheckSuiteProgress(check, "Fixing", 0, 2));
                await check.RemediateAsync(cancellationToken);
                UpdateOperationProgress(new CheckSuiteProgress(check, "Checking", 1, 2));
                await check.EvaluateAsync(cancellationToken);
                UpdateOperationProgress(new CheckSuiteProgress(check, check.IsSatisfied == true ? "Checked" : "Still missing", 2, 2));
            }, cancellationToken);
            HasEverEvaluated = true;
            RefreshStatus();
            CompleteOperation(check.IsSatisfied == true || !check.IsRequired, SummaryText);
        }
        catch (Exception e)
        {
            SummaryText = $"Fix failed: {e.Message}";
            HasMissingRequired = true;
            CompleteOperation(false, SummaryText);
            RaiseHeaderPropertiesChanged();
        }
        finally
        {
            SetBusy(false, SummaryText);
            operationLock.Release();
        }
    }

    public async Task InstallMissingAsync(CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            SetOperationStatus("Prerequisites are already being checked or installed.");
            return;
        }

        try
        {
            BeginOperation("Installing prerequisites", "Starting installation...");
            SetBusy(true, "Installing missing prerequisites...");
            await RunOffUiThreadAsync(
                () => Suite.RemediateFailedAsync(UpdateOperationProgress, cancellationToken),
                cancellationToken);
            HasEverEvaluated = true;
            RefreshStatus();
            CompleteOperation(!HasMissingRequired, SummaryText);
        }
        catch (Exception e)
        {
            SummaryText = $"Install failed: {e.Message}";
            HasMissingRequired = true;
            CompleteOperation(false, SummaryText);
            RaiseHeaderPropertiesChanged();
        }
        finally
        {
            SetBusy(false, SummaryText);
            operationLock.Release();
        }
    }

    public async Task SetCheckPrerequisitesAtStartup(bool value)
    {
        if (CheckPrerequisitesAtStartup == value)
        {
            return;
        }

        var config = configProvider.ActualConfig with
        {
            CheckPrerequisitesAtStartup = value
        };
        configProvider.Save(config);
        CheckPrerequisitesAtStartup = value;
        RaisePropertyChanged(nameof(CheckPrerequisitesAtStartup));
    }

    public async Task OpenToolsFolder()
    {
        Toolchain.EnsureBaseDirectories();
        await ProcessUtils.OpenFolder(Toolchain.ToolsRoot);
    }

    public void ClearAllLogs()
    {
        foreach (var check in Suite.Checks.Items)
        {
            check.ClearDiagnostics();
        }

        RaisePropertyChanged(nameof(HasAnyLogs));
    }

    public string BuildAllLogsText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Prerequisites");
        builder.AppendLine(SummaryText);
        builder.AppendLine($"Tools root: {Toolchain.ToolsRoot.FullName}");
        builder.AppendLine();

        foreach (var check in Suite.Checks.Items)
        {
            builder.AppendLine($"[{check.StatusText}] {check.Title}");
            if (!string.IsNullOrWhiteSpace(check.Details))
            {
                builder.AppendLine(check.Details);
            }

            if (check.IsBlocked)
            {
                builder.AppendLine(check.DependencyStatusText);
            }

            if (!string.IsNullOrWhiteSpace(check.ProgressStatus))
            {
                builder.AppendLine(check.ProgressStatus);
            }

            if (check.LastError != null)
            {
                builder.AppendLine($"Error: {check.LastError.Value.Message}");
            }

            if (!string.IsNullOrWhiteSpace(check.LastOutput))
            {
                builder.AppendLine(check.LastOutput);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
        await EvaluateAllAsync();
    }

    private static Task RunOffUiThreadAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        return Task.Run(operation, cancellationToken);
    }

    private void RefreshStatus()
    {
        MissingRequiredCount = Suite.Checks.Items.Count(x => x.IsRequired && x.IsSatisfied == false);
        HasMissingRequired = Suite.Checks.Items.Any(x => x.IsRequired && x.IsSatisfied != true);
        var checkedCount = Suite.Checks.Items.Count(x => x.IsSatisfied != null);
        SummaryText = checkedCount == 0
            ? "Not checked yet"
            : MissingRequiredCount == 0
                ? "Required tools are ready"
                : $"{MissingRequiredCount} required prerequisite(s) need attention";
        RaiseHeaderPropertiesChanged();
    }

    private void BeginOperation(string title, string status)
    {
        OperationTitleText = title;
        OperationStatusText = status;
        OperationProgressPercent = 0;
        OperationFailed = false;
        RaiseOperationPropertiesChanged();
    }

    private void UpdateOperationProgress(CheckSuiteProgress progress)
    {
        OperationStatusText = progress.Message;
        OperationProgressPercent = progress.Percent;
        RaiseOperationPropertiesChanged();
    }

    private void CompleteOperation(bool success, string status)
    {
        OperationFailed = !success;
        if (success)
        {
            OperationProgressPercent = 100;
        }

        OperationStatusText = status;
        RaiseOperationPropertiesChanged();
    }

    private void SetOperationStatus(string status)
    {
        OperationStatusText = status;
        RaiseOperationPropertiesChanged();
    }

    private void SetBusy(bool value, string summaryText)
    {
        IsOperationBusy = value;
        if (!string.IsNullOrWhiteSpace(summaryText))
        {
            SummaryText = summaryText;
        }
        RaisePropertyChanged(
            nameof(IsOperationBusy),
            nameof(SummaryText),
            nameof(HeaderStatusText),
            nameof(HeaderStatusClass),
            nameof(OperationProgressClass),
            nameof(ShowProgressStrip),
            nameof(OperationProgressVisibilityClass));
    }

    private void RaiseHeaderPropertiesChanged()
    {
        RaisePropertyChanged(
            nameof(HasMissingRequired),
            nameof(MissingRequiredCount),
            nameof(SummaryText),
            nameof(HasEverEvaluated),
            nameof(HeaderStatusText),
            nameof(HeaderStatusClass));
    }

    private void RaiseOperationPropertiesChanged()
    {
        RaisePropertyChanged(
            nameof(OperationTitleText),
            nameof(OperationStatusText),
            nameof(OperationProgressPercent),
            nameof(OperationProgressText),
            nameof(OperationProgressStyle),
            nameof(OperationProgressClass),
            nameof(OperationProgressVisibilityClass),
            nameof(OperationFailed),
            nameof(ShowProgressStrip));
    }
}
