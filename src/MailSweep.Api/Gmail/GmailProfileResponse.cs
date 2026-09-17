namespace MailSweep.Api.Gmail;

public sealed record GmailProfileResponse(string EmailAddress, long MessagesTotal, long ThreadsTotal);
