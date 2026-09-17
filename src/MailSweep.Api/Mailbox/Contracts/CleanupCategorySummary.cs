namespace MailSweep.Api.Mailbox.Contracts;

public sealed record CleanupCategorySummary(
    CleanupCategory Category,
    long MessageCount,
    long EstimatedBytes);
