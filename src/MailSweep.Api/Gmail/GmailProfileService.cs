using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

namespace MailSweep.Api.Gmail;

internal sealed class GmailProfileService(IGoogleAuthProvider authProvider) : IGmailProfileService
{
    public async Task<GmailProfileResponse> GetProfileAsync(CancellationToken cancellationToken)
    {
        Google.Apis.Auth.OAuth2.GoogleCredential credential;
        try
        {
            credential = await authProvider.GetCredentialAsync(cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is null)
        {
            throw new GmailCredentialMissingException();
        }
        using var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "MailSweep"
        });

        var profile = await gmail.Users.GetProfile("me").ExecuteAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(profile.EmailAddress) ||
            profile.MessagesTotal is null || profile.ThreadsTotal is null)
        {
            throw new InvalidOperationException("Gmail returned an incomplete profile.");
        }

        return new GmailProfileResponse(
            profile.EmailAddress, profile.MessagesTotal.Value, profile.ThreadsTotal.Value);
    }
}
