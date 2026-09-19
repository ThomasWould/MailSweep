using System.Security.Claims;
using System.Text.Encodings.Web;
using Google.Apis.Auth.AspNetCore3;
using MailSweep.Api.Gmail;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MailSweep.Api.Tests;

internal sealed class MailSweepApiFactory(
    Exception? profileException = null,
    string environmentName = "Testing",
    GmailMetadataProbeResponse? metadataProbeResponse = null) : WebApplicationFactory<Program>
{
    private int profileCallCount;
    public int ProfileCallCount => profileCallCount;

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost:7119"),
        AllowAutoRedirect = false
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "test-client-id",
                ["Authentication:Google:ClientSecret"] = "test-client-secret",
                ["Frontend:Origin"] = "https://localhost:5173"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IGmailProfileService>();
            services.AddScoped<IGmailProfileService>(_ => new FakeGmailProfileService(
                profileException, () => Interlocked.Increment(ref profileCallCount)));
            if (metadataProbeResponse is not null)
            {
                services.RemoveAll<IGmailMetadataProbe>();
                services.AddScoped<IGmailMetadataProbe>(_ => new FakeGmailMetadataProbe(metadataProbeResponse));
            }
            services.PostConfigure<OpenIdConnectOptions>(
                GoogleOpenIdConnectDefaults.AuthenticationScheme,
                options => options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration
                        {
                            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                            TokenEndpoint = "https://oauth2.googleapis.com/token",
                            Issuer = "https://accounts.google.com"
                        }));
        });
    }

    private sealed class FakeGmailMetadataProbe(GmailMetadataProbeResponse response) : IGmailMetadataProbe
    {
        public Task<GmailMetadataProbeResponse> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class FakeGmailProfileService(Exception? failure, Action onCall) : IGmailProfileService
    {
        public Task<GmailProfileResponse> GetProfileAsync(CancellationToken cancellationToken)
        {
            onCall();
            return failure is null
                ? Task.FromResult(new GmailProfileResponse("test@example.com", 123, 45))
                : Task.FromException<GmailProfileResponse>(failure);
        }
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuthentication";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var email) ||
            string.IsNullOrWhiteSpace(email.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email.ToString())], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
