using PoeShared.Scaffolding;
using Shouldly;
using YoloEase.UI.Core;

namespace YoloEase.Tests.UI.Core;

/// <summary>
/// Verifies the concurrency contract shared by refreshable view models.
/// </summary>
public class RefreshableReactiveObjectFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// WHAT: Ensures many overlapping refresh requests collapse into one active run plus one follow-up.
    /// HOW: Blocks the first refresh, queues many callers, then verifies only the queued follow-up executes.
    /// </summary>
    [Test]
    public async Task ShouldCoalesceManyOverlappingRequestsIntoSingleFollowUp()
    {
        // Given
        var firstStarted = CreateCompletion();
        var firstCanFinish = CreateCompletion();
        var followUpStarted = CreateCompletion();
        var instance = new TestRefreshable
        {
            OnRefresh = async (_, refreshNumber) =>
            {
                if (refreshNumber == 1)
                {
                    firstStarted.TrySetResult();
                    await firstCanFinish.Task;
                }
                else if (refreshNumber == 2)
                {
                    followUpStarted.TrySetResult();
                }
            },
        };

        // When
        var firstRefresh = instance.Refresh();
        await firstStarted.Task.WaitAsync(Timeout);

        var overlappingRefreshes = Enumerable
            .Range(0, 20)
            .Select(_ => instance.Refresh())
            .ToArray();

        firstCanFinish.TrySetResult();

        await followUpStarted.Task.WaitAsync(Timeout);
        await Task.WhenAll(overlappingRefreshes.Append(firstRefresh)).WaitAsync(Timeout);

        // Then
        instance.RefreshCount.ShouldBe(2);
        instance.IsBusy.ShouldBeFalse();
    }

    /// <summary>
    /// WHAT: Ensures recursive refresh requests schedule follow-up work without deadlocking.
    /// HOW: Requests another refresh from inside the first refresh and waits for both passes to finish.
    /// </summary>
    [Test]
    public async Task ShouldNotDeadlockWhenRefreshIsRequestedFromRefreshInternal()
    {
        // Given
        var instance = new TestRefreshable();
        instance.OnRefresh = async (self, refreshNumber) =>
        {
            if (refreshNumber == 1)
            {
                await self.Refresh();
            }
        };

        // When
        await instance.Refresh().WaitAsync(Timeout);

        // Then
        instance.RefreshCount.ShouldBe(2);
        instance.IsBusy.ShouldBeFalse();
    }

    /// <summary>
    /// WHAT: Ensures a failing refresh faults all current callers but does not poison future refreshes.
    /// HOW: Blocks the first refresh until an overlapping caller is waiting, then throws and retries successfully.
    /// </summary>
    [Test]
    public async Task ShouldPropagateFailureToOverlappingCallersAndAllowFutureRefresh()
    {
        // Given
        var firstStarted = CreateCompletion();
        var firstCanFail = CreateCompletion();
        var instance = new TestRefreshable
        {
            OnRefresh = async (_, refreshNumber) =>
            {
                if (refreshNumber == 1)
                {
                    firstStarted.TrySetResult();
                    await firstCanFail.Task;
                    throw new InvalidOperationException("refresh failed");
                }
            },
        };

        // When
        var firstRefresh = instance.Refresh();
        await firstStarted.Task.WaitAsync(Timeout);
        var overlappingRefresh = instance.Refresh();
        firstCanFail.TrySetResult();

        var firstError = await Should.ThrowAsync<InvalidOperationException>(async () => await firstRefresh);
        var overlappingError = await Should.ThrowAsync<InvalidOperationException>(async () => await overlappingRefresh);
        firstError.Message.ShouldBe("refresh failed");
        overlappingError.Message.ShouldBe("refresh failed");

        instance.OnRefresh = (_, _) => Task.CompletedTask;
        await instance.Refresh().WaitAsync(Timeout);

        // Then
        instance.RefreshCount.ShouldBe(2);
        instance.IsBusy.ShouldBeFalse();
    }

    /// <summary>
    /// WHAT: Ensures the busy flag remains true across a coalesced follow-up refresh.
    /// HOW: Releases the first refresh while holding the follow-up and checks busy state before final completion.
    /// </summary>
    [Test]
    public async Task ShouldKeepBusyStateDuringCoalescedFollowUp()
    {
        // Given
        var firstStarted = CreateCompletion();
        var firstCanFinish = CreateCompletion();
        var followUpStarted = CreateCompletion();
        var followUpCanFinish = CreateCompletion();
        var instance = new TestRefreshable
        {
            OnRefresh = async (_, refreshNumber) =>
            {
                if (refreshNumber == 1)
                {
                    firstStarted.TrySetResult();
                    await firstCanFinish.Task;
                }
                else if (refreshNumber == 2)
                {
                    followUpStarted.TrySetResult();
                    await followUpCanFinish.Task;
                }
            },
        };

        // When
        var firstRefresh = instance.Refresh();
        await firstStarted.Task.WaitAsync(Timeout);
        instance.IsBusy.ShouldBeTrue();

        var overlappingRefresh = instance.Refresh();
        firstCanFinish.TrySetResult();
        await followUpStarted.Task.WaitAsync(Timeout);
        instance.IsBusy.ShouldBeTrue();

        followUpCanFinish.TrySetResult();
        await Task.WhenAll(firstRefresh, overlappingRefresh).WaitAsync(Timeout);

        // Then
        instance.RefreshCount.ShouldBe(2);
        instance.IsBusy.ShouldBeFalse();
    }

    private static TaskCompletionSource CreateCompletion()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestRefreshable : RefreshableReactiveObject
    {
        private int refreshCount;

        public int RefreshCount => Volatile.Read(ref refreshCount);

        public Func<TestRefreshable, int, Task> OnRefresh { get; set; } = (_, _) => Task.CompletedTask;

        protected override Task RefreshInternal(IProgressReporter? progressReporter = default)
        {
            var refreshNumber = Interlocked.Increment(ref refreshCount);
            return OnRefresh(this, refreshNumber);
        }
    }
}
