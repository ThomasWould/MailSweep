using Google.Apis.Auth.AspNetCore3;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Gmail;

internal sealed class GmailMailboxScanSourceFactory(IGoogleAuthProvider authProvider) :
    IGmailMailboxScanSourceFactory
{
    private static readonly TimeSpan RequiredCredentialLifetime =
        MailboxScanPlan.JobDuration + TimeSpan.FromSeconds(30);

    public async Task<IMailboxScanSource> CreateAsync(CancellationToken cancellationToken)
    {
        var client = await AuthenticatedGmailMessageApiClient.CreateForBackgroundScanAsync(
            authProvider, RequiredCredentialLifetime, cancellationToken);
        return new GmailMailboxScanSource(client);
    }
}
