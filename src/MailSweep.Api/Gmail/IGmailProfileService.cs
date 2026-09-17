namespace MailSweep.Api.Gmail;

public interface IGmailProfileService
{
    Task<GmailProfileResponse> GetProfileAsync(CancellationToken cancellationToken);
}
