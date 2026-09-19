using System.Security.Claims;
using MailSweep.Api.Gmail;
using MailSweep.Api.Mailbox.Contracts;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Mailbox;

internal static class MailboxScanEndpoints
{
    public static void MapMailboxScanEndpoints(this WebApplication app, string webOrigin)
    {
        app.MapPost("/api/mailbox/scans", async (
            HttpContext context,
            MailboxScanService scans,
            IGmailMailboxScanSourceFactory sourceFactory,
            IGmailProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            NoStore(context);
            if (!HasTrustedOrigin(context, webOrigin))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var accountId = GetAccountId(context.User);
            if (accountId is null)
                return Results.Unauthorized();

            IMailboxScanSource? source = null;
            GmailProfileResponse profile;
            try
            {
                profile = await profileService.GetProfileAsync(cancellationToken);
                source = await sourceFactory.CreateAsync(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                var reconnectRequired = GmailProfileFailure.RequiresReconnect(exception);
                return Results.Json(
                    new MailboxScanErrorResponse(
                        reconnectRequired ? "reconnect_required" : "temporarily_unavailable"),
                    statusCode: reconnectRequired
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var progress = scans.Start(accountId, source, profile.MessagesTotal);
                source = null; // Ownership transferred to the background job.
                return Results.Accepted($"/api/mailbox/scans/{progress.ScanId}", progress);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new MailboxScanErrorResponse("scan_already_active_or_capacity_reached"),
                    statusCode: StatusCodes.Status409Conflict);
            }
            finally
            {
                source?.Dispose();
            }
        }).RequireAuthorization();

        app.MapGet("/api/mailbox/scans/{id:guid}", (
            Guid id,
            HttpContext context,
            MailboxScanService scans) =>
        {
            NoStore(context);
            var accountId = GetAccountId(context.User);
            if (accountId is null)
                return Results.Unauthorized();
            var progress = scans.GetProgress(accountId, id);
            return progress is null ? Results.NotFound() : Results.Ok(progress);
        }).RequireAuthorization();

        app.MapGet("/api/mailbox/scans/{id:guid}/summary", (
            Guid id,
            HttpContext context,
            MailboxScanService scans) =>
        {
            NoStore(context);
            var accountId = GetAccountId(context.User);
            if (accountId is null)
                return Results.Unauthorized();
            var progress = scans.GetProgress(accountId, id);
            if (progress is null)
                return Results.NotFound();
            var summary = scans.GetResult(accountId, id);
            return summary is null
                ? Results.Json(new MailboxScanErrorResponse("summary_not_available"),
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(summary);
        }).RequireAuthorization();

        app.MapPost("/api/mailbox/scans/{id:guid}/cancel", async (
            Guid id,
            HttpContext context,
            MailboxScanService scans) =>
        {
            NoStore(context);
            if (!HasTrustedOrigin(context, webOrigin))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var accountId = GetAccountId(context.User);
            if (accountId is null)
                return Results.Unauthorized();
            var progress = await scans.CancelAsync(accountId, id);
            return progress is null ? Results.NotFound() : Results.Accepted(value: progress);
        }).RequireAuthorization();
    }

    private static bool HasTrustedOrigin(HttpContext context, string webOrigin) =>
        string.Equals(context.Request.Headers.Origin.ToString(), webOrigin, StringComparison.Ordinal);

    private static string? GetAccountId(ClaimsPrincipal user)
    {
        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(subject))
            return $"google:{subject}";

        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        return string.IsNullOrWhiteSpace(email) ? null : $"email:{email.Trim().ToUpperInvariant()}";
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private sealed record MailboxScanErrorResponse(string Error);
}
