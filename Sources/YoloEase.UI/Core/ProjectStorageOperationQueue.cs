using System.Diagnostics;
using System.Threading;

namespace YoloEase.UI.Core;

/// <summary>
/// Serializes project storage operations for one loaded project instance.
/// </summary>
internal sealed class ProjectStorageOperationQueue : IDisposable
{
    private static readonly TimeSpan LongWaitThreshold = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly AsyncLocal<int> reentrancyDepth = new();
    private readonly Action<string>? debug;
    private readonly Action<string>? warn;

    public ProjectStorageOperationQueue(Action<string>? debug = null, Action<string>? warn = null)
    {
        this.debug = debug;
        this.warn = warn;
    }

    public Task Run(string operationName, Func<Task> operation)
    {
        return Run<object?>(operationName, async () =>
        {
            await operation();
            return null;
        });
    }

    public async Task<T> Run<T>(string operationName, Func<Task<T>> operation)
    {
        if (reentrancyDepth.Value > 0)
        {
            return await operation();
        }

        var stopwatch = Stopwatch.StartNew();
        await gate.WaitAsync();
        var waitedFor = stopwatch.Elapsed;
        if (waitedFor >= LongWaitThreshold)
        {
            warn?.Invoke($"Project storage operation '{operationName}' waited {waitedFor.TotalMilliseconds:F0} ms for the storage queue");
        }
        else
        {
            debug?.Invoke($"Project storage operation '{operationName}' entered the storage queue after {waitedFor.TotalMilliseconds:F0} ms");
        }

        reentrancyDepth.Value++;
        try
        {
            return await operation();
        }
        finally
        {
            reentrancyDepth.Value--;
            gate.Release();
        }
    }

    public void Dispose()
    {
        // Intentionally do not dispose the gate. Project disposal can race with an in-flight storage
        // operation unwinding through finally; disposing SemaphoreSlim can turn a harmless close into
        // ObjectDisposedException during Release().
    }
}
