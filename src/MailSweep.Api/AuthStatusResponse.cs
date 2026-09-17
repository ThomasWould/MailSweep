namespace MailSweep.Api;

public sealed record AuthStatusResponse(bool Authenticated, string? EmailAddress);
