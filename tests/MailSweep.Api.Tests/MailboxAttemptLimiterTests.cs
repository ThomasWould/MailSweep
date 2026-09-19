using MailSweep.Api.Mailbox.Contracts;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class MailboxAttemptLimiterTests
{
    [Fact]
    public async Task AttemptsAreSmoothlySpacedIncludingFirstAttempt()
    {
        var h = new ScanHarness();
        using var limiter = new MailboxAttemptLimiter(h.Clock, h.DelayAsync);
        for (var i = 1; i <= 10; i++)
        {
            await limiter.WaitAsync(CancellationToken.None);
            Assert.Equal(TimeSpan.FromMilliseconds(i * 500), h.Clock.GetUtcNow() - ScanHarness.Started);
        }
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        var before = h.Clock.GetUtcNow();
        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromMilliseconds(500), h.Clock.GetUtcNow() - before);
    }

    [Fact]
    public async Task CancellationInterruptsLimiterWaitAndDoesNotLeakGate()
    {
        var h = new ScanHarness();
        using var limiter = new MailboxAttemptLimiter(h.Clock);
        using var cancellation = new CancellationTokenSource();
        var pending = limiter.WaitAsync(cancellation.Token).AsTask();
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var next = limiter.WaitAsync(CancellationToken.None).AsTask();
        h.Clock.Advance(TimeSpan.FromMilliseconds(500));
        await next;
    }

    [Fact]
    public void BackoffIsExponentialWithJitterAndHonorsRetryAfterMinimum()
    {
        var policy = new MailboxRetryPolicy(() => 0.25);
        Assert.Equal(TimeSpan.FromSeconds(1.25), policy.GetDelay(1, null));
        Assert.Equal(TimeSpan.FromSeconds(2.25), policy.GetDelay(2, null));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(1, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(1.25), policy.GetDelay(1, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public async Task RetryAfterCannotBypassJobDeadline()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b"]);
        h.Source.Get = (_, _) => throw new MailboxSourceException(MailboxSourceFailure.Transient, TimeSpan.FromMinutes(6));
        var outcome = await h.RunAsync();
        Assert.Single(h.Source.Gets);
        Assert.Equal(MailboxScanStatus.Completed, outcome.Progress.Status);
        Assert.True(outcome.Progress.LimitedByBudget);
        Assert.Equal("scan_deadline_reached", outcome.Progress.StatusReason);
    }

    [Fact]
    public async Task CancellationInterruptsRetryBackoffWithoutAnotherAttempt()
    {
        var h = new ScanHarness();
        using var cancellation = new CancellationTokenSource();
        h.Source.Cohorts(["a"]);
        h.Source.Get = (_, _) => throw new MailboxSourceException(MailboxSourceFailure.Transient);
        Task Delay(TimeSpan duration, CancellationToken token)
        {
            if (duration >= TimeSpan.FromSeconds(1)) cancellation.Cancel();
            return h.DelayAsync(duration, token);
        }
        var engine = new MailboxScanEngine(h.Clock, new(() => 0), Delay);
        var outcome = await engine.RunAsync(Guid.NewGuid(), h.Source, 0, cancellationToken: cancellation.Token);
        Assert.Single(h.Source.Gets);
        Assert.Equal(MailboxScanStatus.Cancelled, outcome.Progress.Status);
    }

    [Fact]
    public async Task SingleWorkerNeverOverlapsMetadataRequests()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b"]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        h.Source.Get = async (id, token) =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            Interlocked.Decrement(ref active);
            return ScanHarness.Metadata(id);
        };
        var running = h.RunAsync();
        await entered.Task;
        Assert.Single(h.Source.Gets);
        release.SetResult();
        Assert.Equal(2, (await running).Progress.GetSucceeded);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task ExhaustedIdStillPausesNextIdForRetryAfter()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["failed", "good"]);
        var starts = new List<DateTimeOffset>();
        h.Source.Get = (id, _) =>
        {
            starts.Add(h.Clock.GetUtcNow());
            return id == "failed"
                ? throw new MailboxSourceException(MailboxSourceFailure.Transient, TimeSpan.FromSeconds(10))
                : Task.FromResult(ScanHarness.Metadata(id));
        };
        var outcome = await h.RunAsync();
        Assert.Equal(4, starts.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), starts[3] - starts[2]);
        Assert.Equal(1, outcome.Progress.GetSucceeded);
    }

    [Fact]
    public async Task RealDelayImplementationUsesInjectedTimeForDeadlineCancellation()
    {
        var h = new ScanHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Source.List = async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new([], null);
        };
        var engine = new MailboxScanEngine(h.Clock);
        var running = engine.RunAsync(Guid.NewGuid(), h.Source, 0);
        await entered.Task;
        h.Clock.Advance(MailboxScanPlan.JobDuration);
        var outcome = await running;
        Assert.Equal(MailboxScanStatus.Completed, outcome.Progress.Status);
        Assert.Equal("scan_deadline_reached", outcome.Progress.StatusReason);
    }

    [Fact]
    public async Task CohortTimeoutCarriesRemainingRetryAfterIntoNextCohort()
    {
        var h = new ScanHarness();
        var starts = new List<DateTimeOffset>();
        h.Source.List = (_, _) =>
        {
            starts.Add(h.Clock.GetUtcNow());
            return starts.Count == 1
                ? throw new MailboxSourceException(MailboxSourceFailure.Transient, TimeSpan.FromSeconds(60))
                : Task.FromResult(new MailboxIdPage([], null));
        };
        Task Delay(TimeSpan duration, CancellationToken token)
        {
            if (duration == TimeSpan.FromSeconds(60))
            {
                h.Clock.Advance(MailboxScanPlan.CohortDuration);
                token.ThrowIfCancellationRequested();
            }
            return h.DelayAsync(duration, token);
        }
        var engine = new MailboxScanEngine(h.Clock, new(() => 0), Delay);
        var outcome = await engine.RunAsync(Guid.NewGuid(), h.Source, 0);
        Assert.Equal(3, starts.Count);
        Assert.Equal(TimeSpan.FromSeconds(60), starts[1] - starts[0]);
        Assert.Equal(CohortEnumerationStatus.Truncated, outcome.Progress.Cohorts[0].EnumerationStatus);
        Assert.True(outcome.Progress.Cohorts[1].CountComplete);
        Assert.True(outcome.Progress.LimitedByBudget);
    }
}
