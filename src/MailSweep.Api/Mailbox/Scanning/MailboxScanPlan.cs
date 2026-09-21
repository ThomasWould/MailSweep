using System.Globalization;
using MailSweep.Api.Mailbox.Contracts;

namespace MailSweep.Api.Mailbox.Scanning;

public sealed record MailboxCohortQuery(MailboxScanCohort Cohort, string Query, int ReservedAttempts);

public sealed class MailboxScanPlan
{
    public const int GetAttemptBudget = 100;
    public const int PageBudget = 20;
    public const int ListAttemptBudget = 24;
    public const int IdBudget = 10_000;
    public static readonly TimeSpan JobDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CohortDuration = TimeSpan.FromSeconds(45);

    public MailboxScanPlan(DateTimeOffset startedAt)
    {
        StartedAt = startedAt.ToUniversalTime();
        TwoYearCutoff = StartedAt.AddYears(-2).ToUnixTimeSeconds();
        OneYearCutoff = StartedAt.AddYears(-1).ToUnixTimeSeconds();
        const string exclusions = "-is:starred -is:important -in:sent -in:drafts";
        Queries = Array.AsReadOnly(new[]
        {
            new MailboxCohortQuery(MailboxScanCohort.LargeMail, $"larger:25M {exclusions}", 60),
            new MailboxCohortQuery(MailboxScanCohort.OldPromotions,
                $"category:promotions before:{TwoYearCutoff.ToString(CultureInfo.InvariantCulture)} {exclusions}", 25),
            new MailboxCohortQuery(MailboxScanCohort.OldUnreadInbox,
                $"is:unread in:inbox before:{OneYearCutoff.ToString(CultureInfo.InvariantCulture)} {exclusions}", 15)
        });
    }

    public DateTimeOffset StartedAt { get; }
    public long TwoYearCutoff { get; }
    public long OneYearCutoff { get; }
    public IReadOnlyList<MailboxCohortQuery> Queries { get; }
}
