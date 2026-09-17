namespace MailSweep.Api.Mailbox.Contracts;

public enum MailboxScanStage
{
    Profile,
    Enumerating,
    Enriching,
    Aggregating,
    Analyzing
}
