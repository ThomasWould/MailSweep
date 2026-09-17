namespace MailSweep.Api.Mailbox.Contracts;

public sealed record SenderSummary(
    string Key,
    string? EmailAddress,
    string? Domain,
    string? DisplayName,
    long MessageCount,
    long EstimatedBytes);
