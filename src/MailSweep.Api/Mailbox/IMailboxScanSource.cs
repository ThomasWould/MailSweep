namespace MailSweep.Api.Mailbox;

public interface IMailboxScanSource
{
    Task<MailboxMessagePage> ListMessagePageAsync(
        string query,
        string? pageToken,
        CancellationToken cancellationToken);

    Task<MailboxMessageMetadata> GetMessageMetadataAsync(
        string messageId,
        CancellationToken cancellationToken);
}
