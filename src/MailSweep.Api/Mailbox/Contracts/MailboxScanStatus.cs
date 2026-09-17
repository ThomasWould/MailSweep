namespace MailSweep.Api.Mailbox.Contracts;

public enum MailboxScanStatus
{
    Queued,
    Running,
    Completed,
    Cancelled,
    Failed
}
