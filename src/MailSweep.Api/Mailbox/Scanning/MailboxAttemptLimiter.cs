namespace MailSweep.Api.Mailbox.Scanning;

public interface IMailboxAttemptLimiter
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

/// <summary>One shared limiter per job. Smooth pacing, with no initial burst.
/// The engine uses one async worker; this gate also serializes concurrent waiters.</summary>
public sealed class MailboxAttemptLimiter(TimeProvider timeProvider,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null) : IMailboxAttemptLimiter, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long? lastAttempt;

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var wait = TimeSpan.FromMilliseconds(500) -
                (lastAttempt is { } last ? timeProvider.GetElapsedTime(last) : TimeSpan.Zero);
            if (wait > TimeSpan.Zero)
                await (delayAsync?.Invoke(wait, cancellationToken) ?? Task.Delay(wait, timeProvider, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            lastAttempt = timeProvider.GetTimestamp();
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}

public sealed class MailboxRetryPolicy(Func<double>? jitter = null)
{
    public TimeSpan GetDelay(int failedAttempt, TimeSpan? retryAfter)
    {
        var exponential = TimeSpan.FromSeconds(Math.Pow(2, failedAttempt - 1) +
            Math.Clamp((jitter ?? Random.Shared.NextDouble)(), 0, 1));
        return retryAfter is { } minimum && minimum > exponential ? minimum : exponential;
    }
}
