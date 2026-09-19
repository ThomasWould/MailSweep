using Google.Apis.Gmail.v1.Data;

namespace MailSweep.Api.Gmail;

internal interface IGmailMessageApiClient
{
    Task<ListMessagesResponse> ListMessagesAsync(
        GmailListMessagesRequest request,
        CancellationToken cancellationToken);

    Task<Message> GetMessageAsync(
        GmailGetMessageRequest request,
        CancellationToken cancellationToken);
}
