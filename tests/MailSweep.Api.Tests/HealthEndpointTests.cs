using System.Net;
using System.Net.Http.Json;
using MailSweep.Api;

namespace MailSweep.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealthReturnsHealthyApplicationStatus()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new HealthResponse("healthy", "MailSweep"),
            await response.Content.ReadFromJsonAsync<HealthResponse>());
    }
}
