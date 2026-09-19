using System.Net;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Requests;
using MailSweep.Api.Gmail;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class GmailMailboxScanSourceTests
{
    [Fact]
    public async Task ListBuildsBoundedReadOnlyRequestAndMapsPage()
    {
        var client = new FakeGmailMessageApiClient
        {
            ListResponse = new ListMessagesResponse
            {
                Messages =
                [
                    new Message { Id = "message-1", ThreadId = "thread-1" },
                    new Message { Id = " ", ThreadId = "ignored" }
                ],
                NextPageToken = "next-token",
                ResultSizeEstimate = 999_999
            }
        };
        var source = new GmailMailboxScanSource(client);

        var page = await source.ListAsync(
            new MailboxListRequest("larger:25M", "page-token"), CancellationToken.None);

        var request = Assert.IsType<GmailListMessagesRequest>(client.ListRequest);
        Assert.Equal("larger:25M", request.Query);
        Assert.Equal("page-token", request.PageToken);
        Assert.Equal(500, request.MaxResults);
        Assert.False(request.IncludeSpamTrash);
        Assert.Equal("messages(id,threadId),nextPageToken,resultSizeEstimate", request.Fields);
        Assert.Equal(["message-1"], page.MessageIds);
        Assert.Equal("next-token", page.NextPageToken);
    }

    [Fact]
    public async Task MetadataUsesSelectedHeadersAndMapsScanFields()
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(
                internalDate: 1_725_000_000_000,
                sizeEstimate: 27_000_000,
                from: "Example Sender <sender@example.com>",
                subject: "Monthly update")
        };
        var source = new GmailMailboxScanSource(client);

        var metadata = await source.GetMetadataAsync("message-1", CancellationToken.None);

        var request = Assert.IsType<GmailGetMessageRequest>(client.GetRequest);
        Assert.Equal("message-1", request.MessageId);
        Assert.Equal(UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata, request.Format);
        Assert.Equal(["From", "Subject"], request.MetadataHeaders);
        Assert.Equal("id,threadId,labelIds,internalDate,sizeEstimate,payload/headers", request.Fields);
        Assert.Equal("message-1", metadata.MessageId);
        Assert.Equal("thread-1", metadata.ThreadId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_725_000_000_000), metadata.ReceivedAt);
        Assert.Equal(27_000_000, metadata.EstimatedBytes);
        Assert.Equal(["INBOX", "IMPORTANT"], metadata.Labels);
        Assert.Equal("Example Sender <sender@example.com>", metadata.From);
        Assert.Equal("Monthly update", metadata.Subject);
    }

    [Fact]
    public async Task MissingInternalDateRemainsMissing()
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(internalDate: null, sizeEstimate: 100, from: null, subject: null)
        };

        var metadata = await new GmailMailboxScanSource(client)
            .GetMetadataAsync("message-1", CancellationToken.None);

        Assert.Null(metadata.ReceivedAt);
    }

    [Fact]
    public async Task MissingSizeEstimateRemainsMissing()
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(internalDate: 1_725_000_000_000, sizeEstimate: null, from: null, subject: null)
        };

        var metadata = await new GmailMailboxScanSource(client)
            .GetMetadataAsync("message-1", CancellationToken.None);

        Assert.Null(metadata.EstimatedBytes);
    }

    [Fact]
    public async Task MissingSubjectRemainsNull()
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(1_725_000_000_000, 100, "sender@example.com", null)
        };

        var metadata = await new GmailMailboxScanSource(client)
            .GetMetadataAsync("message-1", CancellationToken.None);

        Assert.Null(metadata.Subject);
    }

    [Fact]
    public async Task CancellationIsPassedToGoogleFacingClient()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new FakeGmailMessageApiClient { HonorCancellation = true };
        var source = new GmailMailboxScanSource(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.ListAsync(new MailboxListRequest("in:inbox", null), cancellation.Token));
        Assert.Equal(cancellation.Token, client.LastCancellationToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, MailboxSourceFailure.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, "authError", MailboxSourceFailure.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, "insufficientPermissions", MailboxSourceFailure.Permission)]
    [InlineData(HttpStatusCode.Forbidden, "userRateLimitExceeded", MailboxSourceFailure.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, null, MailboxSourceFailure.Transient)]
    [InlineData(HttpStatusCode.InternalServerError, null, MailboxSourceFailure.Transient)]
    [InlineData(HttpStatusCode.NotFound, null, MailboxSourceFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, null, MailboxSourceFailure.InvalidRequest)]
    public async Task MapsGoogleFailuresIntoEngineFailureModel(
        HttpStatusCode status,
        string? reason,
        MailboxSourceFailure expected)
    {
        var error = new GoogleApiException("Gmail")
        {
            HttpStatusCode = status,
            Error = reason is null ? null : new RequestError
            {
                Errors = [new SingleError { Reason = reason }]
            }
        };
        var source = new GmailMailboxScanSource(new FakeGmailMessageApiClient { ListException = error });

        var thrown = await Assert.ThrowsAsync<MailboxSourceException>(() =>
            source.ListAsync(new MailboxListRequest("in:inbox", null), CancellationToken.None));

        Assert.Equal(expected, thrown.Failure);
        Assert.Equal("Mailbox source request failed.", thrown.Message);
    }

    [Fact]
    public async Task MapsHttpTimeoutToTransientWithoutMaskingCallerCancellation()
    {
        var source = new GmailMailboxScanSource(new FakeGmailMessageApiClient
        {
            ListException = new TaskCanceledException("HTTP timeout")
        });

        var thrown = await Assert.ThrowsAsync<MailboxSourceException>(() =>
            source.ListAsync(new MailboxListRequest("in:inbox", null), CancellationToken.None));

        Assert.Equal(MailboxSourceFailure.Transient, thrown.Failure);
    }

    private static Message CreateMessage(
        long? internalDate,
        int? sizeEstimate,
        string? from,
        string? subject)
    {
        var headers = new List<MessagePartHeader>();
        if (from is not null)
        {
            headers.Add(new MessagePartHeader { Name = "From", Value = from });
        }

        if (subject is not null)
        {
            headers.Add(new MessagePartHeader { Name = "Subject", Value = subject });
        }

        return new Message
        {
            Id = "message-1",
            ThreadId = "thread-1",
            InternalDate = internalDate,
            SizeEstimate = sizeEstimate,
            LabelIds = ["INBOX", "IMPORTANT"],
            Payload = new MessagePart { Headers = headers }
        };
    }
}

internal sealed class FakeGmailMessageApiClient : IGmailMessageApiClient
{
    public ListMessagesResponse ListResponse { get; init; } = new();
    public Message MessageResponse { get; init; } = new();
    public bool HonorCancellation { get; init; }
    public Exception? ListException { get; init; }
    public Exception? GetException { get; init; }
    public GmailListMessagesRequest? ListRequest { get; private set; }
    public GmailGetMessageRequest? GetRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public Task<ListMessagesResponse> ListMessagesAsync(
        GmailListMessagesRequest request,
        CancellationToken cancellationToken)
    {
        ListRequest = request;
        LastCancellationToken = cancellationToken;
        if (ListException is not null)
            return Task.FromException<ListMessagesResponse>(ListException);
        return HonorCancellation
            ? Task.FromCanceled<ListMessagesResponse>(cancellationToken)
            : Task.FromResult(ListResponse);
    }

    public Task<Message> GetMessageAsync(
        GmailGetMessageRequest request,
        CancellationToken cancellationToken)
    {
        GetRequest = request;
        LastCancellationToken = cancellationToken;
        if (GetException is not null)
            return Task.FromException<Message>(GetException);
        return HonorCancellation
            ? Task.FromCanceled<Message>(cancellationToken)
            : Task.FromResult(MessageResponse);
    }
}
