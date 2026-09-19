using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using MailSweep.Api.Gmail;

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

        var page = await source.ListMessagePageAsync("larger:25M", "page-token", CancellationToken.None);

        var request = Assert.IsType<GmailListMessagesRequest>(client.ListRequest);
        Assert.Equal("larger:25M", request.Query);
        Assert.Equal("page-token", request.PageToken);
        Assert.Equal(500, request.MaxResults);
        Assert.False(request.IncludeSpamTrash);
        Assert.Equal("messages(id,threadId),nextPageToken,resultSizeEstimate", request.Fields);
        Assert.Equal([new Mailbox.MailboxMessageReference("message-1", "thread-1")], page.Messages);
        Assert.Equal("next-token", page.NextPageToken);
        Assert.True(page.HasNextPage);
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

        var metadata = await source.GetMessageMetadataAsync("message-1", CancellationToken.None);

        var request = Assert.IsType<GmailGetMessageRequest>(client.GetRequest);
        Assert.Equal("message-1", request.MessageId);
        Assert.Equal(UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata, request.Format);
        Assert.Equal(["From", "Subject"], request.MetadataHeaders);
        Assert.Equal("id,threadId,labelIds,internalDate,sizeEstimate,payload/headers", request.Fields);
        Assert.Equal("message-1", metadata.MessageId);
        Assert.Equal("thread-1", metadata.ThreadId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_725_000_000_000), metadata.InternalDate);
        Assert.Equal(27_000_000, metadata.EstimatedSizeBytes);
        Assert.Equal(["INBOX", "IMPORTANT"], metadata.Labels);
        Assert.Equal("Example Sender <sender@example.com>", metadata.FromHeader);
        Assert.Equal("sender@example.com", metadata.SenderEmail);
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
            .GetMessageMetadataAsync("message-1", CancellationToken.None);

        Assert.Null(metadata.InternalDate);
    }

    [Fact]
    public async Task MissingSizeEstimateRemainsMissing()
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(internalDate: 1_725_000_000_000, sizeEstimate: null, from: null, subject: null)
        };

        var metadata = await new GmailMailboxScanSource(client)
            .GetMessageMetadataAsync("message-1", CancellationToken.None);

        Assert.Null(metadata.EstimatedSizeBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad@@example.com")]
    public async Task MissingOrMalformedFromDoesNotInventSender(string? from)
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(1_725_000_000_000, 100, from, null)
        };

        var metadata = await new GmailMailboxScanSource(client)
            .GetMessageMetadataAsync("message-1", CancellationToken.None);

        Assert.Null(metadata.SenderEmail);
    }

    [Fact]
    public async Task MissingSubjectRemainsNull()
    {
        var client = new FakeGmailMessageApiClient
        {
            MessageResponse = CreateMessage(1_725_000_000_000, 100, "sender@example.com", null)
        };

        var metadata = await new GmailMailboxScanSource(client)
            .GetMessageMetadataAsync("message-1", CancellationToken.None);

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
            source.ListMessagePageAsync("in:inbox", null, cancellation.Token));
        Assert.Equal(cancellation.Token, client.LastCancellationToken);
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
    public GmailListMessagesRequest? ListRequest { get; private set; }
    public GmailGetMessageRequest? GetRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public Task<ListMessagesResponse> ListMessagesAsync(
        GmailListMessagesRequest request,
        CancellationToken cancellationToken)
    {
        ListRequest = request;
        LastCancellationToken = cancellationToken;
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
        return HonorCancellation
            ? Task.FromCanceled<Message>(cancellationToken)
            : Task.FromResult(MessageResponse);
    }
}
