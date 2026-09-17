using System.Net;
using System.Net.Http.Json;
using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Gmail.v1;
using MailSweep.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MailSweep.Api.Tests;

public sealed class AuthEndpointTests
{
    [Fact]
    public async Task ConnectChallengesGoogleWithOnlyReadOnlyGmailScope()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/auth/google/connect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("accounts.google.com", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("https://localhost:7119/signin-google", query["redirect_uri"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal(
            ["openid", "email", "profile", GmailService.Scope.GmailReadonly],
            query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task AnonymousStatusIsDisconnected()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/auth/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new AuthStatusResponse(false, null),
            await response.Content.ReadFromJsonAsync<AuthStatusResponse>());
    }

    [Fact]
    public async Task AuthenticatedStatusIncludesEmailAddress()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");

        using var response = await client.GetAsync("/api/auth/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new AuthStatusResponse(true, "test@example.com"),
            await response.Content.ReadFromJsonAsync<AuthStatusResponse>());
    }

    [Fact]
    public async Task LogoutRequiresSessionAndTrustedOrigin()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();

        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        anonymousRequest.Headers.Add("Origin", "http://localhost:5173");
        using var anonymousResponse = await client.SendAsync(anonymousRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-User", "test@example.com");
        using var untrustedResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Forbidden, untrustedResponse.StatusCode);

        using var trustedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        trustedRequest.Headers.Add("Origin", "http://localhost:5173");
        using var trustedResponse = await client.SendAsync(trustedRequest);
        Assert.Equal(HttpStatusCode.NoContent, trustedResponse.StatusCode);
        Assert.Contains(trustedResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-MailSweep=", StringComparison.Ordinal));
    }

    [Fact]
    public void AuthenticationUsesOnlyIdentityAndGmailReadOnlyScopes()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();

        var google = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(GoogleOpenIdConnectDefaults.AuthenticationScheme);
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("test-client-id", google.ClientId);
        Assert.Equal("/signin-google", google.CallbackPath);
        Assert.Equal(["openid", "email", "profile", GmailService.Scope.GmailReadonly], google.Scope);
        Assert.True(google.UsePkce);
        Assert.Equal("__Host-MailSweep", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.None, cookie.Cookie.SameSite);
    }

    [Fact]
    public async Task CorsAllowsOnlyFrontendOriginWithCredentials()
    {
        using var factory = new MailSweepApiFactory();
        using var client = factory.CreateHttpsClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/auth/status");
        allowed.Headers.Add("Origin", "http://localhost:5173");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        using var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal("http://localhost:5173",
            Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true",
            Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Credentials")));

        using var logoutPreflight = new HttpRequestMessage(HttpMethod.Options, "/api/auth/logout");
        logoutPreflight.Headers.Add("Origin", "http://localhost:5173");
        logoutPreflight.Headers.Add("Access-Control-Request-Method", "POST");
        logoutPreflight.Headers.Add("Access-Control-Request-Headers", "x-mailsweep-request");
        using var logoutPreflightResponse = await client.SendAsync(logoutPreflight);
        Assert.Equal("http://localhost:5173",
            Assert.Single(logoutPreflightResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("x-mailsweep-request",
            Assert.Single(logoutPreflightResponse.Headers.GetValues("Access-Control-Allow-Headers")));

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "/api/auth/status");
        rejected.Headers.Add("Origin", "https://untrusted.example");
        rejected.Headers.Add("Access-Control-Request-Method", "GET");
        using var rejectedResponse = await client.SendAsync(rejected);
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
