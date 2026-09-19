namespace MailSweep.Api.Mailbox;

public sealed record MailboxMessageReference(string MessageId, string? ThreadId);
