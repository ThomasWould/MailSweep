namespace MailSweep.Api.Mailbox;

public sealed record MailboxMessagePage(
    IReadOnlyList<MailboxMessageReference> Messages,
    string? NextPageToken)
{
    public bool HasNextPage => !string.IsNullOrWhiteSpace(NextPageToken);
}
