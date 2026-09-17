namespace MailSweep.Api;

public static class FrontendOrigin
{
    public static string Read(IConfiguration configuration)
    {
        var value = configuration["Frontend:Origin"];
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            value != uri.GetLeftPart(UriPartial.Authority))
        {
            throw new InvalidOperationException("Frontend:Origin must be an HTTPS origin without a path or query.");
        }

        return value;
    }
}
