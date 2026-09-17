namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxScanCohortResult(
    MailboxScanCohort Cohort,
    CohortEnumerationStatus EnumerationStatus,
    int PagesEnumerated,
    long ObservedCandidateCount,
    int EnrichedMessages,
    long EstimatedMatchingMessageBytes)
{
    public bool CountComplete => EnumerationStatus == CohortEnumerationStatus.Completed;
}
