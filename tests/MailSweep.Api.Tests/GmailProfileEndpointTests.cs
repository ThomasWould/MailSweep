using System.Net;
using System.Net.Http.Json;
using MailSweep.Api.Gmail;

namespace MailSweep.Api.Tests;

public sealed class GmailProfileEndpointTests
{
    [Fact]
    public async Task AnonymousProfileRequestIsRejected()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedProfileReturnsOnlyMailboxSummary()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new GmailProfileResponse("test@example.com", 123, 45),
            await response.Content.ReadFromJsonAsync<GmailProfileResponse>());
    }
}
