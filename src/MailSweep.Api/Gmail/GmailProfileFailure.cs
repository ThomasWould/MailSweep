using System.Net;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace MailSweep.Api.Gmail;

public static class GmailProfileFailure
{
    public static bool RequiresReconnect(Exception exception) => exception switch
    {
        GmailCredentialMissingException => true,
        // The Google provider wraps rejected refresh requests in InvalidOperationException.
        InvalidOperationException { InnerException: HttpRequestException refreshError } =>
            refreshError.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
        TokenResponseException tokenError => tokenError.Error?.Error == "invalid_grant",
        GoogleApiException apiError when apiError.HttpStatusCode == HttpStatusCode.Unauthorized => true,
        GoogleApiException apiError when apiError.HttpStatusCode == HttpStatusCode.Forbidden =>
            apiError.Error?.Errors?.Any(error =>
                error.Reason is "authError" or "insufficientPermissions") == true,
        _ => false
    };
}

public sealed class GmailCredentialMissingException : Exception;
