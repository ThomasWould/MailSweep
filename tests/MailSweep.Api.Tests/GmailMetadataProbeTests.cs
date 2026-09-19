using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using MailSweep.Api.Gmail;

namespace MailSweep.Api.Tests;

public sealed class GmailMetadataProbeTests
{
    [Fact]
    public async Task EmptyMailboxReturnsOnlyNegativeStructuralFlags()
    {
        var client = new FakeGmailMessageApiClient();

        var result = await new GmailMetadataProbe(client).ProbeAsync(CancellationToken.None);

        Assert.Equal(new GmailMetadataProbeResponse(false, false, false, false, false), result);
        Assert.Null(client.GetRequest);
        var list = Assert.IsType<GmailListMessagesRequest>(client.ListRequest);
        Assert.Equal(1, list.MaxResults);
        Assert.False(list.IncludeSpamTrash);
        Assert.Equal("messages(id)", list.Fields);
    }

    [Fact]
    public async Task ProbeUsesScannerMetadataRequestAndReportsPresenceOnly()
    {
        var client = new FakeGmailMessageApiClient
        {
            ListResponse = new ListMessagesResponse
            {
                Messages = [new Message { Id = "sensitive-message-id" }]
            },
            MessageResponse = new Message
            {
                Id = "sensitive-message-id",
                InternalDate = 1_725_000_000_000,
                SizeEstimate = 123,
                Payload = new MessagePart
                {
                    Headers =
                    [
                        new MessagePartHeader { Name = "From", Value = "Private Person <private@example.com>" },
                        new MessagePartHeader { Name = "Subject", Value = "Private subject" }
                    ]
                }
            }
        };

        var result = await new GmailMetadataProbe(client).ProbeAsync(CancellationToken.None);

        Assert.Equal(new GmailMetadataProbeResponse(true, true, true, true, true), result);
        var get = Assert.IsType<GmailGetMessageRequest>(client.GetRequest);
        Assert.Equal(UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata, get.Format);
        Assert.Equal(["From", "Subject"], get.MetadataHeaders);
        Assert.Equal(GmailMailboxScanSource.MetadataFields, get.Fields);
    }
}
