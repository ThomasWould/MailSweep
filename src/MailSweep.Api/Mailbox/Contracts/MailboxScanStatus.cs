namespace MailSweep.Api.Mailbox.Contracts;

public enum MailboxScanStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Cancelled,
    Failed
}
