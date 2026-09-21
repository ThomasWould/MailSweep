using MailSweep.Api.Mailbox.Contracts;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class MailboxScanServiceTests
{
    private static FakeScanSource WaitingSource(TaskCompletionSource? entered = null) => new()
    {
        List = async (_, token) =>
        {
            entered?.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new([], null);
        }
    };

    [Fact]
    public async Task StartReturnsQueuedAndOwnershipAppliesToProgressResultsAndCancellation()
    {
        var h = new ScanHarness();
        await using var service = h.Service;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = service.Start("owner", WaitingSource(entered), 12);
        Assert.Equal(MailboxScanStatus.Queued, queued.Status);
        await entered.Task;
        Assert.Equal(MailboxScanStatus.Running, service.GetProgress("owner", queued.ScanId)!.Status);
        Assert.Null(service.GetProgress("other", queued.ScanId));
        Assert.Null(service.GetResult("other", queued.ScanId));
        Assert.Null(await service.CancelAsync("other", queued.ScanId));
        Assert.Equal(MailboxScanStatus.Running, service.GetProgress("owner", queued.ScanId)!.Status);
        Assert.Null(service.GetResult("owner", queued.ScanId));
        var cancelled = await service.CancelAsync("owner", queued.ScanId);
        Assert.Equal(MailboxScanStatus.Cancelled, cancelled!.Status);
        Assert.Null(service.GetResult("owner", queued.ScanId));
        Assert.Equal(cancelled, await service.CancelAsync("owner", queued.ScanId));
    }

    [Fact]
    public async Task RejectsDuplicateAccountAndBoundsConcurrentJobsWithoutAQueue()
    {
        var h = new ScanHarness();
        await using var service = h.Service;
        var first = service.Start("a", WaitingSource(), 0);
        Assert.Throws<InvalidOperationException>(() => service.Start("a", WaitingSource(), 0));
        for (var i = 1; i < MailboxScanService.MaxActiveJobs; i++)
            service.Start($"account-{i}", WaitingSource(), 0);
        Assert.Throws<InvalidOperationException>(() => service.Start("extra", WaitingSource(), 0));
        await service.CancelAsync("a", first.ScanId);
        Assert.NotNull(service.Start("a", WaitingSource(), 0));
    }

    [Fact]
    public async Task CompletedJobProvidesSummaryAndExpiresAfterThirtyMinutes()
    {
        var h = new ScanHarness();
        await using var service = h.Service;
        var queued = service.Start("a", h.Source, 123);
        // Poll lifecycle only; pacing/deadline tests exclusively use virtual time.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (service.GetResult("a", queued.ScanId) is null)
            await Task.Delay(1, timeout.Token);
        var result = service.GetResult("a", queued.ScanId)!;
        Assert.Equal(123, result.ProfileMessageCount);
        Assert.Equal(MailboxScanStatus.Completed, service.GetProgress("a", queued.ScanId)!.Status);
        Assert.Empty(result.MessagePreviews);
        await service.CancelAsync("a", queued.ScanId); // Also waits for completion bookkeeping.
        h.Clock.Advance(MailboxScanService.Retention - TimeSpan.FromTicks(1));
        Assert.NotNull(service.GetResult("a", queued.ScanId));
        h.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(service.GetResult("a", queued.ScanId));
        Assert.Null(service.GetProgress("a", queued.ScanId));
        Assert.Equal(123, result.ProfileMessageCount); // Retained snapshots remain valid after cleanup.
    }

    [Fact]
    public async Task RetainedJobsAreBoundedAndExpiredJobsReleaseCapacity()
    {
        var h = new ScanHarness();
        await using var service = h.Service;
        for (var i = 0; i < MailboxScanService.MaxRetainedJobs; i++)
        {
            var job = service.Start("a", WaitingSource(), 0);
            await service.CancelAsync("a", job.ScanId);
        }
        Assert.Throws<InvalidOperationException>(() => service.Start("a", WaitingSource(), 0));
        h.Clock.Advance(MailboxScanService.Retention);
        Assert.NotNull(service.Start("a", WaitingSource(), 0));
    }

    [Fact]
    public async Task ConcurrentStartsForSameAccountAdmitExactlyOneJob()
    {
        var h = new ScanHarness();
        await using var service = h.Service;
        var starts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try { return service.Start("same", WaitingSource(), 0); }
            catch (InvalidOperationException) { return null; }
        })));
        Assert.Single(starts.OfType<MailboxScanProgress>());
    }

    [Fact]
    public async Task DisposeCancelsActiveWorkAndPreventsFurtherStarts()
    {
        var h = new ScanHarness();
        var service = h.Service;
        var source = WaitingSource();
        service.Start("a", source, 0);
        await service.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => service.Start("a", source, 0));
        await service.DisposeAsync();
    }
}
