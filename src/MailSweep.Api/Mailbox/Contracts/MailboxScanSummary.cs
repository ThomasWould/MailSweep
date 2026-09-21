namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxScanSummary(
    Guid ScanId,
    string RuleVersion,
    long ProfileMessageCount,
    IReadOnlyList<MailboxScanCohortResult> Cohorts,
    long UniqueIdsEnumerated,
    int MessagesAttempted,
    int GetAttemptsUsed,
    int GetAttemptBudget,
    int GetSucceeded,
    long EstimatedMatchingMessageBytes,
    bool LimitedByBudget,
    PromotionInsightsSummary PromotionInsights,
    IReadOnlyList<MailboxMessagePreview> MessagePreviews,
    DateTimeOffset CompletedAt);
