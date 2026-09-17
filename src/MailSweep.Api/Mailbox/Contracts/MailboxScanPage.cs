namespace MailSweep.Api.Mailbox.Contracts;

public sealed record MailboxScanPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool IsTruncated);
