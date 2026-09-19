using System.Net;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Gmail;

internal sealed class GmailMailboxScanSource(IGmailMessageApiClient client) : IMailboxScanSource
{
    internal const string ListFields = "messages(id,threadId),nextPageToken,resultSizeEstimate";
    internal const string MetadataFields =
        "id,threadId,labelIds,internalDate,sizeEstimate,payload/headers";

    public async Task<MailboxIdPage> ListAsync(
        MailboxListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        try
        {
            var response = await client.ListMessagesAsync(new GmailListMessagesRequest(
                request.Query,
                NormalizeOptionalValue(request.PageToken),
                request.MaxResults,
                request.IncludeSpamTrash,
                ListFields), cancellationToken);
            var ids = response.Messages?
                .Select(message => message.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray() ?? [];
            return new MailboxIdPage(ids, NormalizeOptionalValue(response.NextPageToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MailboxSourceException(MailboxSourceFailure.Transient);
        }
        catch (Exception exception) when (TryMapFailure(exception, out var failure))
        {
            throw new MailboxSourceException(failure);
        }
    }

    public async Task<MailboxMessageMetadata> GetMetadataAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        try
        {
            var message = await client.GetMessageAsync(CreateMetadataRequest(messageId), cancellationToken);
            return MapMetadata(message, messageId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MailboxSourceException(MailboxSourceFailure.Transient);
        }
        catch (Exception exception) when (TryMapFailure(exception, out var failure))
        {
            throw new MailboxSourceException(failure);
        }
    }

    public void Dispose()
    {
        if (client is IDisposable disposable)
            disposable.Dispose();
    }

    internal static GmailGetMessageRequest CreateMetadataRequest(string messageId) => new(
        messageId,
        UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata,
        ["From", "Subject"],
        MetadataFields);

    internal static MailboxMessageMetadata MapMetadata(Message message, string requestedMessageId) => new(
        string.IsNullOrWhiteSpace(message.Id) ? requestedMessageId : message.Id,
        NormalizeOptionalValue(message.ThreadId) ?? string.Empty,
        ParseInternalDate(message.InternalDate),
        message.SizeEstimate,
        message.LabelIds?.Where(label => !string.IsNullOrWhiteSpace(label)).ToArray() ?? [],
        FindHeader(message, "From"),
        FindHeader(message, "Subject"));

    internal static bool TryMapFailure(Exception exception, out MailboxSourceFailure failure)
    {
        if (exception is GmailCredentialMissingException)
        {
            failure = MailboxSourceFailure.Authentication;
            return true;
        }

        if (exception is HttpRequestException requestException)
        {
            failure = requestException.StatusCode switch
            {
                HttpStatusCode.BadRequest => MailboxSourceFailure.InvalidRequest,
                HttpStatusCode.Unauthorized => MailboxSourceFailure.Authentication,
                HttpStatusCode.Forbidden => MailboxSourceFailure.Permission,
                HttpStatusCode.NotFound => MailboxSourceFailure.Unavailable,
                HttpStatusCode.TooManyRequests => MailboxSourceFailure.Transient,
                >= HttpStatusCode.InternalServerError => MailboxSourceFailure.Transient,
                _ when requestException.StatusCode is null => MailboxSourceFailure.Transient,
                _ => MailboxSourceFailure.InvalidRequest
            };
            return true;
        }

        if (exception is GoogleApiException apiException)
        {
            var reasons = apiException.Error?.Errors?
                .Select(error => error.Reason)
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            failure = apiException.HttpStatusCode switch
            {
                HttpStatusCode.BadRequest => MailboxSourceFailure.InvalidRequest,
                HttpStatusCode.Unauthorized => MailboxSourceFailure.Authentication,
                HttpStatusCode.Forbidden when reasons.Overlaps(
                    ["rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded"]) =>
                    MailboxSourceFailure.Transient,
                HttpStatusCode.Forbidden when reasons.Contains("authError") =>
                    MailboxSourceFailure.Authentication,
                HttpStatusCode.Forbidden => MailboxSourceFailure.Permission,
                HttpStatusCode.NotFound => MailboxSourceFailure.Unavailable,
                HttpStatusCode.TooManyRequests => MailboxSourceFailure.Transient,
                >= HttpStatusCode.InternalServerError => MailboxSourceFailure.Transient,
                _ => MailboxSourceFailure.InvalidRequest
            };
            return true;
        }

        failure = default;
        return false;
    }

    private static string? FindHeader(Message message, string name) => message.Payload?.Headers?
        .FirstOrDefault(header => string.Equals(header?.Name, name, StringComparison.OrdinalIgnoreCase))
        ?.Value;

    private static DateTimeOffset? ParseInternalDate(long? unixMilliseconds)
    {
        if (unixMilliseconds is null)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
