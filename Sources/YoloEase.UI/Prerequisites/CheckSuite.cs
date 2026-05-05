using System.Linq;
using System.Reactive.Disposables;
using System.Threading;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Runs prerequisite checks and remediation steps sequentially while reporting aggregate progress.
/// </summary>
public sealed class CheckSuite : DisposableReactiveObject
{
    private readonly SourceCache<CheckItem, string> checksSource = new(x => x.Id);
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public CheckSuite()
    {
        Checks = checksSource.AsObservableList().AddTo(Anchors);
    }

    public string Id { get; } = $"CheckSuite-{Guid.NewGuid()}";

    public IObservableList<CheckItem> Checks { get; }

    public bool IsBusy { get; private set; }

    public IDisposable AddCheck(CheckItem check)
    {
        checksSource.AddOrUpdate(check);
        return Disposable.Create(() => checksSource.Remove(check));
    }

    public async Task EvaluateAllAsync(CancellationToken cancellationToken = default)
    {
        await EvaluateAllAsync(null, cancellationToken);
    }

    public async Task EvaluateAllAsync(Action<CheckSuiteProgress> progressHandler, CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            SetBusy(true);
            var checks = Checks.Items.ToArray();
            var completedSteps = 0;
            var totalSteps = Math.Max(1, checks.Length);
            foreach (var check in checks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progressHandler?.Invoke(new CheckSuiteProgress(check, "Checking", completedSteps, totalSteps));
                await check.EvaluateAsync(cancellationToken);
                completedSteps++;
                progressHandler?.Invoke(new CheckSuiteProgress(check, "Checked", completedSteps, totalSteps));
            }
        }
        finally
        {
            SetBusy(false);
            operationLock.Release();
        }
    }

    public async Task RemediateFailedAsync(CancellationToken cancellationToken = default)
    {
        await RemediateFailedAsync(null, cancellationToken);
    }

    public async Task RemediateFailedAsync(Action<CheckSuiteProgress> progressHandler, CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            SetBusy(true);
            var requiredChecks = Checks.Items.Where(x => x.IsRequired && x.IncludeInBulkInstall).ToArray();
            var completedSteps = 0;
            var totalSteps = Math.Max(1, requiredChecks.Length * 3);
            foreach (var check in requiredChecks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progressHandler?.Invoke(new CheckSuiteProgress(check, "Checking", completedSteps, totalSteps));
                var before = await check.EvaluateAsync(cancellationToken);
                completedSteps++;
                progressHandler?.Invoke(new CheckSuiteProgress(check, "Checked", completedSteps, totalSteps));
                if (before == true || !check.CanRemediate)
                {
                    completedSteps += 2;
                    progressHandler?.Invoke(new CheckSuiteProgress(check, before == true ? "Already ready" : check.IsBlocked ? "Blocked" : "No fix available", completedSteps, totalSteps));
                    continue;
                }

                progressHandler?.Invoke(new CheckSuiteProgress(check, "Installing", completedSteps, totalSteps));
                await check.RemediateAsync(cancellationToken);
                completedSteps++;
                progressHandler?.Invoke(new CheckSuiteProgress(check, check.LastError == null ? "Installed" : "Install failed", completedSteps, totalSteps));
                progressHandler?.Invoke(new CheckSuiteProgress(check, "Verifying", completedSteps, totalSteps));
                var after = await check.EvaluateAsync(cancellationToken);
                completedSteps++;
                progressHandler?.Invoke(new CheckSuiteProgress(check, after == true ? "Verified" : "Still missing", completedSteps, totalSteps));
            }
        }
        finally
        {
            SetBusy(false);
            operationLock.Release();
        }
    }

    public bool HasMissingRequired => Checks.Items.Any(x => x.IsRequired && x.IsSatisfied != true);

    private void SetBusy(bool value)
    {
        IsBusy = value;
        RaisePropertyChanged(nameof(IsBusy));
    }
}

/// <summary>
/// Reports the suite-level progress for the current prerequisite row and phase.
/// </summary>
public sealed record CheckSuiteProgress(CheckItem Check, string Phase, int CompletedSteps, int TotalSteps)
{
    public double Percent => TotalSteps <= 0 ? 0 : Math.Clamp((double) CompletedSteps / TotalSteps * 100d, 0d, 100d);

    public string Message => $"{Phase}: {Check.Title}";
}
