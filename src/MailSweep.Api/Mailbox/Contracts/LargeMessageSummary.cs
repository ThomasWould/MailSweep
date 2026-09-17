namespace MailSweep.Api.Mailbox.Contracts;

public sealed record LargeMessageSummary(
    string MessageId,
    string ThreadId,
    DateTimeOffset ReceivedAt,
    long EstimatedBytes,
    string? From,
    string? Subject);
