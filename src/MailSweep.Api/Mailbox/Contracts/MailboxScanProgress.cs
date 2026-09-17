namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxScanProgress(
    Guid ScanId,
    MailboxScanStatus Status,
    MailboxScanStage? Stage,
    long EnumeratedMessages,
    long EnrichedMessages,
    long? EstimatedMessages,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? StatusReason);
