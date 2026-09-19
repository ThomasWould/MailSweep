using Google.Apis.Gmail.v1;

namespace MailSweep.Api.Gmail;

internal sealed record GmailListMessagesRequest(
    string? Query,
    string? PageToken,
    long MaxResults,
    bool IncludeSpamTrash,
    string Fields);

internal sealed record GmailGetMessageRequest(
    string MessageId,
    UsersResource.MessagesResource.GetRequest.FormatEnum Format,
    string[] MetadataHeaders,
    string Fields);
