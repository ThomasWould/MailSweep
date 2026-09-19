namespace MailSweep.Api.Gmail;

internal sealed class GmailMetadataProbe(IGmailMessageApiClient client) : IGmailMetadataProbe
{
    private const string ProbeListFields = "messages(id)";

    public async Task<GmailMetadataProbeResponse> ProbeAsync(CancellationToken cancellationToken)
    {
        var page = await client.ListMessagesAsync(
            new GmailListMessagesRequest(
                Query: null,
                PageToken: null,
                MaxResults: 1,
                IncludeSpamTrash: false,
                ProbeListFields),
            cancellationToken);
        var messageId = page.Messages?
            .Select(message => message.Id)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (messageId is null)
        {
            return new GmailMetadataProbeResponse(false, false, false, false, false);
        }

        var message = await client.GetMessageAsync(
            GmailMailboxScanSource.CreateMetadataRequest(messageId),
            cancellationToken);
        var metadata = GmailMailboxScanSource.MapMetadata(message, messageId);
        return new GmailMetadataProbeResponse(
            true,
            metadata.InternalDate is not null,
            metadata.EstimatedSizeBytes is not null,
            metadata.FromHeader is not null,
            metadata.Subject is not null);
    }
}
