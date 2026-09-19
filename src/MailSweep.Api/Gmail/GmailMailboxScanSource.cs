using System.Net.Mail;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using MailSweep.Api.Mailbox;

namespace MailSweep.Api.Gmail;

internal sealed class GmailMailboxScanSource(IGmailMessageApiClient client) : IMailboxScanSource
{
    internal const string ListFields = "messages(id,threadId),nextPageToken,resultSizeEstimate";
    internal const string MetadataFields =
        "id,threadId,labelIds,internalDate,sizeEstimate,payload/headers";

    public async Task<MailboxMessagePage> ListMessagePageAsync(
        string query,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var request = new GmailListMessagesRequest(
            query,
            NormalizeOptionalValue(pageToken),
            MaxResults: 500,
            IncludeSpamTrash: false,
            ListFields);
        var response = await client.ListMessagesAsync(request, cancellationToken);
        var messages = response.Messages?
            .Where(message => !string.IsNullOrWhiteSpace(message.Id))
            .Select(message => new MailboxMessageReference(message.Id, message.ThreadId))
            .ToArray() ?? [];

        return new MailboxMessagePage(messages, NormalizeOptionalValue(response.NextPageToken));
    }

    public async Task<MailboxMessageMetadata> GetMessageMetadataAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var message = await client.GetMessageAsync(CreateMetadataRequest(messageId), cancellationToken);
        return MapMetadata(message, messageId);
    }

    internal static GmailGetMessageRequest CreateMetadataRequest(string messageId) => new(
        messageId,
        UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata,
        ["From", "Subject"],
        MetadataFields);

    internal static MailboxMessageMetadata MapMetadata(Message message, string requestedMessageId)
    {
        var fromHeader = FindHeader(message, "From");
        var subject = FindHeader(message, "Subject");

        return new MailboxMessageMetadata(
            string.IsNullOrWhiteSpace(message.Id) ? requestedMessageId : message.Id,
            NormalizeOptionalValue(message.ThreadId),
            ParseInternalDate(message.InternalDate),
            message.SizeEstimate,
            message.LabelIds?.Where(label => !string.IsNullOrWhiteSpace(label)).ToArray() ?? [],
            fromHeader,
            ParseSenderEmail(fromHeader),
            subject);
    }

    private static string? FindHeader(Message message, string name) => message.Payload?.Headers?
        .FirstOrDefault(header => string.Equals(header?.Name, name, StringComparison.OrdinalIgnoreCase))
        ?.Value;

    private static DateTimeOffset? ParseInternalDate(long? unixMilliseconds)
    {
        if (unixMilliseconds is null)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ParseSenderEmail(string? fromHeader)
    {
        if (string.IsNullOrWhiteSpace(fromHeader) || !MailAddress.TryCreate(fromHeader, out var address))
        {
            return null;
        }

        return address.Address;
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
