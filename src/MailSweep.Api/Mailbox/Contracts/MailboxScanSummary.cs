namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxScanSummary(
    Guid ScanId,
    MailboxScanCoverage Coverage,
    string RuleVersion,
    long ProfileMessageCount,
    long EnumeratedMessages,
    long AnalyzedMessages,
    long UnavailableMessages,
    long EstimatedPotentialReclaimableBytes,
    IReadOnlyList<CleanupCategorySummary> Categories,
    DateTimeOffset CompletedAt);
