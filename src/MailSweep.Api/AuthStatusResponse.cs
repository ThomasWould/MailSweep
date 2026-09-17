namespace MailSweep.Api;

public sealed record AuthStatusResponse(bool Connected, string? EmailAddress);
