using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Gmail;

internal interface IGmailMailboxScanSourceFactory
{
    Task<IMailboxScanSource> CreateAsync(CancellationToken cancellationToken);
}
