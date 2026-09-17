using System.Net;
using System.Net.Http.Json;
using MailSweep.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MailSweep.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealthReturnsHealthyApplicationStatus()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new HealthResponse("healthy", "MailSweep"),
            await response.Content.ReadFromJsonAsync<HealthResponse>());
    }
}
