using System.Security.Claims;
using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Gmail.v1;
using MailSweep.Api;
using MailSweep.Api.Gmail;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

const string webOrigin = "http://localhost:5173";
const string webCorsPolicy = "MailSweepWeb";

builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy(webCorsPolicy, policy => policy
    .WithOrigins(webOrigin)
    .WithMethods("GET", "POST")
    .AllowAnyHeader()
    .AllowCredentials()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-MailSweep";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddGoogleOpenIdConnect(options =>
    {
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CallbackPath = "/signin-google";
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.Scope.Add(GmailService.Scope.GmailReadonly);
    });
builder.Services.AddOptions<OpenIdConnectOptions>(GoogleOpenIdConnectDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Google OAuth credentials must be configured outside the repository.");
        }

        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IGmailProfileService, GmailProfileService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(webCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("healthy", "MailSweep")))
    .WithName("GetHealth");

app.MapGet("/api/auth/google/connect", () => TypedResults.Challenge(
    new AuthenticationProperties { RedirectUri = $"{webOrigin}/?gmailConnected=true" },
    [GoogleOpenIdConnectDefaults.AuthenticationScheme]));

app.MapGet("/api/auth/status", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var authenticated = context.User.Identity?.IsAuthenticated == true;
    var emailAddress = authenticated
        ? context.User.FindFirst(ClaimTypes.Email)?.Value ?? context.User.FindFirst("email")?.Value
        : null;
    return TypedResults.Ok(new AuthStatusResponse(authenticated, emailAddress));
});

app.MapGet("/api/gmail/profile", async (HttpContext context, IGmailProfileService profileService,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return TypedResults.Ok(await profileService.GetProfileAsync(cancellationToken));
}).RequireAuthorization();

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    if (!string.Equals(context.Request.Headers.Origin.ToString(), webOrigin, StringComparison.Ordinal))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Headers.CacheControl = "no-store";
    return Results.NoContent();
}).RequireAuthorization();

app.Run();

public partial class Program { }
