using MailSweep.Api.Mailbox.Contracts;

namespace MailSweep.Api.Mailbox.Scanning;

/// <summary>
/// Instance-local jobs, one active job per account, no queue. Register as a singleton when integrated.
/// Results expire after 30 minutes; admission and lookups opportunistically clean expired jobs.
/// Capacity is bounded even when no requests arrive to trigger cleanup. Restart loses all jobs.
/// </summary>
public sealed class MailboxScanService : IAsyncDisposable
{
    public const int MaxActiveJobs = 4;
    public const int MaxRetainedJobs = 64;
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private readonly object gate = new();
    private readonly Dictionary<Guid, Job> jobs = [];
    private readonly TimeProvider clock;
    private readonly MailboxScanEngine engine;
    private bool disposed;

    public MailboxScanService(TimeProvider? timeProvider = null, MailboxRetryPolicy? retryPolicy = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        clock = timeProvider ?? TimeProvider.System;
        engine = new(clock, retryPolicy, delayAsync);
    }

    // profileMessageCount is supplied by the integration layer; no profile/API/OAuth dependency here.
    // Do not use an HTTP request-aborted token for the job lifetime.
    public MailboxScanProgress Start(string accountId, IMailboxScanSource source, long profileMessageCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(profileMessageCount);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Cleanup();
            var active = jobs.Values.Where(j => !j.Work.IsCompleted).ToArray();
            if (active.Any(j => j.AccountId == accountId))
                throw new InvalidOperationException("An account scan is already active.");
            if (active.Length >= MaxActiveJobs || jobs.Count >= MaxRetainedJobs)
                throw new InvalidOperationException("Scan capacity reached. Try again after a job expires.");
            var job = new Job(accountId);
            jobs.Add(job.Progress.ScanId, job);
            job.Work = ExecuteAsync(job, source, profileMessageCount);
            return job.Progress;
        }
    }

    public MailboxScanProgress? GetProgress(string accountId, Guid scanId)
    {
        lock (gate)
        {
            Cleanup();
            return Find(accountId, scanId)?.Progress;
        }
    }

    public MailboxScanSummary? GetResult(string accountId, Guid scanId)
    {
        lock (gate)
        {
            Cleanup();
            return Find(accountId, scanId)?.Summary;
        }
    }

    public async Task<MailboxScanProgress?> CancelAsync(string accountId, Guid scanId)
    {
        Job? job;
        Task cancellation;
        lock (gate)
        {
            Cleanup();
            job = Find(accountId, scanId);
            if (job is null) return null;
            // CancelAsync schedules callbacks outside this lock. Cleanup waits for these callbacks too.
            cancellation = job.CancellationWork ??= job.Cancellation.CancelAsync();
        }
        await cancellation;
        await job.Work;
        lock (gate) return job.Progress;
    }

    private async Task ExecuteAsync(Job job, IMailboxScanSource source, long profileCount)
    {
        await Task.Yield(); // Start returns promptly; no Task.Run and no blocking worker thread.
        var outcome = await engine.RunAsync(job.Progress.ScanId, source, profileCount,
            progress =>
            {
                // Publish terminal status together with its result below, as one atomic update.
                if (progress.Status == MailboxScanStatus.Running)
                    lock (gate) job.Progress = progress;
            }, job.Cancellation.Token);
        lock (gate)
        {
            job.Progress = outcome.Progress;
            job.Summary = outcome.Summary;
            job.FinishedTimestamp = clock.GetTimestamp();
        }
    }

    private Job? Find(string accountId, Guid scanId) =>
        jobs.TryGetValue(scanId, out var job) && job.AccountId == accountId ? job : null;

    private void Cleanup()
    {
        foreach (var pair in jobs.Where(p => p.Value.Work.IsCompleted &&
                     (p.Value.CancellationWork?.IsCompleted ?? true) &&
                     p.Value.FinishedTimestamp is { } finished && clock.GetElapsedTime(finished) >= Retention).ToArray())
        {
            jobs.Remove(pair.Key);
            pair.Value.Cancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Job[] retained;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            retained = jobs.Values.ToArray();
            foreach (var job in retained)
                job.CancellationWork ??= job.Cancellation.CancelAsync();
        }
        await Task.WhenAll(retained.Select(j => Task.WhenAll(j.Work, j.CancellationWork!)));
        lock (gate)
        {
            foreach (var job in retained) job.Cancellation.Dispose();
            jobs.Clear();
        }
    }

    private sealed class Job(string accountId)
    {
        public string AccountId { get; } = accountId;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? CancellationWork { get; set; }
        public Task Work { get; set; } = Task.CompletedTask;
        public long? FinishedTimestamp { get; set; }
        public MailboxScanSummary? Summary { get; set; }
        public MailboxScanProgress Progress { get; set; } = new(Guid.NewGuid(), MailboxScanStatus.Queued,
            null, Array.AsReadOnly(Enum.GetValues<MailboxScanCohort>().Select(c =>
                new MailboxScanCohortResult(c, CohortEnumerationStatus.NotStarted, 0, 0, 0, 0)).ToArray()),
            0, 0, 0, MailboxScanPlan.GetAttemptBudget, 0, false, null, null, null);
    }
}
