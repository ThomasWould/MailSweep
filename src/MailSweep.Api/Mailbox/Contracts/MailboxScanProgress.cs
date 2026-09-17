namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxScanProgress(
    Guid ScanId,
    MailboxScanStatus Status,
    MailboxScanStage? Stage,
    IReadOnlyList<MailboxScanCohortResult> Cohorts,
    long UniqueIdsEnumerated,
    int MessagesAttempted,
    int GetAttemptsUsed,
    int GetAttemptBudget,
    int GetSucceeded,
    bool LimitedByBudget,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? StatusReason);
