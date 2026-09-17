namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxMessagePreview(
    string MessageId,
    string ThreadId,
    IReadOnlyList<MailboxScanCohort> MatchingCohorts,
    DateTimeOffset ReceivedAt,
    long EstimatedBytes,
    string? From,
    string? Subject);
