using System.Net;
using System.Net.Http.Json;
using MailSweep.Api;
using MailSweep.Api.Gmail;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc;

namespace MailSweep.Api.Tests;

public sealed class CookieSessionIntegrationTests
{
    [Fact]
    public async Task AnonymousRequestsHaveNoSession()
    {
        using var factory = new CookieSessionFactory();
        using var client = factory.CreateHttpsClient();

        using var status = await client.GetAsync("/api/auth/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(new AuthStatusResponse(false, null),
            await status.Content.ReadFromJsonAsync<AuthStatusResponse>());

        using var profile = await client.GetAsync("/api/gmail/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
        Assert.Null(profile.Headers.Location);
    }

    [Fact]
    public async Task GoogleCallbackCreatesSecureCookieThatAuthenticatesLaterRequests()
    {
        using var factory = new CookieSessionFactory();
        using var client = factory.CreateHttpsClient();

        using var callback = await CompleteOfflineGoogleSignInAsync(factory, client,
            "/api/auth/google/connect?returnUrl=https://untrusted.example/");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("http://localhost:5173/?gmailConnected=true", callback.Headers.Location?.ToString());
        var sessionCookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-MailSweep=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", sessionCookie, StringComparison.OrdinalIgnoreCase);

        using var status = await client.GetAsync("/api/auth/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(new AuthStatusResponse(true, "test@example.com"),
            await status.Content.ReadFromJsonAsync<AuthStatusResponse>());

        using var profile = await client.GetAsync("/api/gmail/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(new GmailProfileResponse("test@example.com", 123, 45),
            await profile.Content.ReadFromJsonAsync<GmailProfileResponse>());
    }

    [Fact]
    public async Task LogoutClearsCookieAndSubsequentRequestsAreAnonymous()
    {
        using var factory = new CookieSessionFactory();
        using var client = factory.CreateHttpsClient();
        using var callback = await CompleteOfflineGoogleSignInAsync(factory, client);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        using var rejectedLogout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Forbidden, rejectedLogout.StatusCode);
        using var stillConnected = await client.GetAsync("/api/auth/status");
        Assert.Equal(new AuthStatusResponse(true, "test@example.com"),
            await stillConnected.Content.ReadFromJsonAsync<AuthStatusResponse>());

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("Origin", "http://localhost:5173");
        using var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-MailSweep=", StringComparison.Ordinal));

        using var status = await client.GetAsync("/api/auth/status");
        Assert.Equal(new AuthStatusResponse(false, null),
            await status.Content.ReadFromJsonAsync<AuthStatusResponse>());
        using var profile = await client.GetAsync("/api/gmail/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    public async Task DeniedGoogleConsentReturnsToFixedFrontendWithoutSession()
    {
        using var factory = new CookieSessionFactory();
        using var client = factory.CreateHttpsClient();

        using var challenge = await client.GetAsync(
            "/api/auth/google/connect?returnUrl=https://untrusted.example/");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var query = QueryHelpers.ParseQuery(Assert.IsType<Uri>(challenge.Headers.Location).Query);
        var state = Uri.EscapeDataString(query["state"].ToString());

        using var callback = await client.GetAsync($"/signin-google?error=access_denied&state={state}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("http://localhost:5173/?gmailConnected=false", callback.Headers.Location?.ToString());
        Assert.False(callback.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith("__Host-MailSweep=", StringComparison.Ordinal)));

        using var status = await client.GetAsync("/api/auth/status");
        Assert.Equal(new AuthStatusResponse(false, null),
            await status.Content.ReadFromJsonAsync<AuthStatusResponse>());
    }

    [Fact]
    public async Task GmailServiceFailureReturnsGatewayErrorWithoutLeakingDetails()
    {
        using var factory = new CookieSessionFactory
        {
            ProfileResponse = _ => throw new HttpRequestException("private upstream details")
        };
        using var client = factory.CreateHttpsClient();
        using var callback = await CompleteOfflineGoogleSignInAsync(factory, client);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        using var profile = await client.GetAsync("/api/gmail/profile");
        Assert.Equal(HttpStatusCode.BadGateway, profile.StatusCode);
        var problem = await profile.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Gmail profile unavailable", problem?.Title);
        Assert.DoesNotContain("private upstream details", await profile.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> CompleteOfflineGoogleSignInAsync(
        CookieSessionFactory factory, HttpClient client,
        string connectPath = "/api/auth/google/connect")
    {
        using var challenge = await client.GetAsync(connectPath);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = Assert.IsType<Uri>(challenge.Headers.Location);
        Assert.Equal("issuer.test", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);
        factory.Nonce = query["nonce"].ToString();
        var state = Uri.EscapeDataString(query["state"].ToString());

        return await client.GetAsync($"/signin-google?code=offline-code&state={state}");
    }
}
