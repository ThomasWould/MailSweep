namespace MailSweep.Api.Mailbox;

public sealed record MailboxMessageMetadata(
    string MessageId,
    string? ThreadId,
    DateTimeOffset? InternalDate,
    long? EstimatedSizeBytes,
    IReadOnlyList<string> Labels,
    string? FromHeader,
    string? SenderEmail,
    string? Subject);
