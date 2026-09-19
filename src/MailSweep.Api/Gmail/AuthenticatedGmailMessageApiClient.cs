using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Http;
using Google.Apis.Services;

namespace MailSweep.Api.Gmail;

internal sealed class AuthenticatedGmailMessageApiClient(IGoogleAuthProvider authProvider) :
    IGmailMessageApiClient,
    IDisposable
{
    private readonly SemaphoreSlim serviceLock = new(1, 1);
    private GmailService? gmailService;

    public async Task<ListMessagesResponse> ListMessagesAsync(
        GmailListMessagesRequest request,
        CancellationToken cancellationToken)
    {
        var gmail = await GetServiceAsync(cancellationToken);
        var list = gmail.Users.Messages.List("me");
        list.Q = request.Query;
        list.PageToken = request.PageToken;
        list.MaxResults = request.MaxResults;
        list.IncludeSpamTrash = request.IncludeSpamTrash;
        list.Fields = request.Fields;
        return await list.ExecuteAsync(cancellationToken);
    }

    public async Task<Message> GetMessageAsync(
        GmailGetMessageRequest request,
        CancellationToken cancellationToken)
    {
        var gmail = await GetServiceAsync(cancellationToken);
        var get = gmail.Users.Messages.Get("me", request.MessageId);
        get.Format = request.Format;
        get.MetadataHeaders = request.MetadataHeaders;
        get.Fields = request.Fields;
        return await get.ExecuteAsync(cancellationToken);
    }

    public void Dispose()
    {
        gmailService?.Dispose();
        serviceLock.Dispose();
    }

    private async Task<GmailService> GetServiceAsync(CancellationToken cancellationToken)
    {
        if (gmailService is not null)
        {
            return gmailService;
        }

        await serviceLock.WaitAsync(cancellationToken);
        try
        {
            if (gmailService is not null)
            {
                return gmailService;
            }

            Google.Apis.Auth.OAuth2.GoogleCredential credential;
            try
            {
                credential = await authProvider.GetCredentialAsync(cancellationToken: cancellationToken);
            }
            catch (InvalidOperationException exception) when (exception.InnerException is null)
            {
                throw new GmailCredentialMissingException();
            }

            gmailService = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "MailSweep",
                // Retry accounting belongs to the bounded scan engine; do not hide attempts here.
                DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
            });
            return gmailService;
        }
        finally
        {
            serviceLock.Release();
        }
    }
}
