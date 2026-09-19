namespace MailSweep.Api.Mailbox.Scanning;

/// <summary>
/// Each call performs at most one HTTP attempt, with automatic retries disabled.
/// Implementations must honor cancellation and return metadata only (never bodies).
/// The caller owns the source and its credentials for the entire job lifetime.
/// </summary>
public interface IMailboxScanSource : IDisposable
{
    Task<MailboxIdPage> ListAsync(MailboxListRequest request, CancellationToken cancellationToken);
    Task<MailboxMessageMetadata> GetMetadataAsync(string messageId, CancellationToken cancellationToken);

    void IDisposable.Dispose() { }
}

public sealed record MailboxListRequest(string Query, string? PageToken, int MaxResults = 500,
    bool IncludeSpamTrash = false);

public sealed record MailboxIdPage(IReadOnlyList<string> MessageIds, string? NextPageToken);

public sealed record MailboxMessageMetadata(string MessageId, string ThreadId,
    DateTimeOffset? ReceivedAt, long? EstimatedBytes, IReadOnlyList<string> Labels,
    string? From, string? Subject);

public enum MailboxSourceFailure
{
    Transient,
    Unavailable,
    InvalidRequest,
    Authentication,
    Permission
}

/// <summary>Adapters classify rate limits/429/5xx as Transient, 404 as Unavailable.
/// Other 403s are Permission; invalid requests and authentication are never retried.</summary>
public sealed class MailboxSourceException(MailboxSourceFailure failure, TimeSpan? retryAfter = null)
    : Exception("Mailbox source request failed.")
{
    public MailboxSourceFailure Failure { get; } = failure;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
