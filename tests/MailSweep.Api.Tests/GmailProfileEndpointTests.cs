using System.Net;
using System.Net.Http.Json;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
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

    [Fact]
    public async Task MissingCredentialRequestsReconnectWithoutExposingDetails()
    {
        using var factory = new MailSweepApiFactory(new GmailCredentialMissingException());
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new GmailProfileErrorResponse("reconnect_required"),
            await response.Content.ReadFromJsonAsync<GmailProfileErrorResponse>());
    }

    [Fact]
    public async Task RevokedTokenRequestsReconnect()
    {
        using var factory = new MailSweepApiFactory(
            new TokenResponseException(new TokenErrorResponse { Error = "invalid_grant" }));
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new GmailProfileErrorResponse("reconnect_required"),
            await response.Content.ReadFromJsonAsync<GmailProfileErrorResponse>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.Conflict, "reconnect_required")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "temporarily_unavailable")]
    public async Task WrappedRefreshFailuresAreClassifiedByHttpStatus(
        HttpStatusCode refreshStatus, HttpStatusCode expectedStatus, string expectedCode)
    {
        using var factory = new MailSweepApiFactory(new InvalidOperationException(
            "Refresh failed", new HttpRequestException("Token endpoint failed", null, refreshStatus)));
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(new GmailProfileErrorResponse(expectedCode),
            await response.Content.ReadFromJsonAsync<GmailProfileErrorResponse>());
    }

    [Fact]
    public async Task RejectedAccessTokenRequestsReconnect()
    {
        using var factory = new MailSweepApiFactory(new GoogleApiException("Gmail")
        {
            HttpStatusCode = HttpStatusCode.Unauthorized
        });
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task TransientGoogleFailureCanBeRetried()
    {
        using var factory = new MailSweepApiFactory(new GoogleApiException("Gmail")
        {
            HttpStatusCode = HttpStatusCode.ServiceUnavailable
        });
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(new GmailProfileErrorResponse("temporarily_unavailable"),
            await response.Content.ReadFromJsonAsync<GmailProfileErrorResponse>());
    }

    [Fact]
    public async Task ForbiddenGoogleFailureWithoutAuthReasonCanBeRetried()
    {
        using var factory = new MailSweepApiFactory(new GoogleApiException("Gmail")
        {
            HttpStatusCode = HttpStatusCode.Forbidden
        });
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/gmail/profile");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
