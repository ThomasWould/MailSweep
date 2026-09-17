using System.Net;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.AspNetCore3;
using MailSweep.Api.Gmail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MailSweep.Api.Tests;

internal sealed class CookieSessionFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "https://issuer.test";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly FakeGoogleBackchannel _backchannel;

    public CookieSessionFactory() => _backchannel = new FakeGoogleBackchannel(this);

    public string? Nonce { get; set; }
    public Func<CancellationToken, Task<GmailProfileResponse>> ProfileResponse { get; set; } =
        _ => Task.FromResult(new GmailProfileResponse("test@example.com", 123, 45));

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost:7119"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "test-client-id",
                ["Authentication:Google:ClientSecret"] = "test-client-secret",
                ["Frontend:Origin"] = "https://localhost:5173"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGmailProfileService>();
            services.AddScoped<IGmailProfileService>(_ => new FakeGmailProfileService(this));
            services.PostConfigure<OpenIdConnectOptions>(
                GoogleOpenIdConnectDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = "https://issuer.test/authorize",
                        TokenEndpoint = "https://issuer.test/token",
                        Issuer = Issuer
                    };
                    configuration.SigningKeys.Add(new RsaSecurityKey(_rsa));
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.Backchannel = new HttpClient(_backchannel);
                });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _rsa.Dispose();
        }
    }

    private string CreateIdToken()
    {
        if (string.IsNullOrEmpty(Nonce))
        {
            throw new InvalidOperationException("The OIDC challenge nonce was not captured.");
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "test-client-id",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "offline-user"),
                new Claim("email", "test@example.com"),
                new Claim("nonce", Nonce)
            ]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class FakeGoogleBackchannel(CookieSessionFactory factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri != "https://issuer.test/token" ||
                request.Method != HttpMethod.Post)
            {
                throw new InvalidOperationException($"Unexpected OAuth backchannel request: {request.Method} {request.RequestUri}");
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "offline-access-token",
                    token_type = "Bearer",
                    expires_in = 3600,
                    id_token = factory.CreateIdToken()
                }), Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeGmailProfileService(CookieSessionFactory factory) : IGmailProfileService
    {
        public Task<GmailProfileResponse> GetProfileAsync(CancellationToken cancellationToken) =>
            factory.ProfileResponse(cancellationToken);
    }
}
