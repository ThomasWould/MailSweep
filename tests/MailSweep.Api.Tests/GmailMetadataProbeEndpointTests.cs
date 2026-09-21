using System.Net;
using System.Net.Http.Json;
using MailSweep.Api.Gmail;

namespace MailSweep.Api.Tests;

public sealed class GmailMetadataProbeEndpointTests
{
    [Fact]
    public async Task ProbeIsNotMappedOutsideDevelopment()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/dev/gmail/metadata-probe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentProbeRequiresAuthentication()
    {
        using var factory = new MailSweepApiFactory(
            environmentName: "Development",
            metadataProbeResponse: new GmailMetadataProbeResponse(true, true, true, true, true));
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/dev/gmail/metadata-probe");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentProbeReturnsOnlyStructuralFlags()
    {
        var expected = new GmailMetadataProbeResponse(true, true, false, true, false);
        using var factory = new MailSweepApiFactory(
            environmentName: "Development",
            metadataProbeResponse: expected);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/dev/gmail/metadata-probe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<GmailMetadataProbeResponse>());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
