using Microsoft.Extensions.Configuration;

namespace LmsCompanion.Mcp;

public static class ConnectionManagementUri
{
    public static string? Resolve(IConfiguration configuration)
    {
        var portal = configuration["Frontend:ConnectionManagementUri"];
        if (!Uri.TryCreate(portal, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri.AbsoluteUri;
        }

        if (!configuration.GetValue<bool>("Frontend:AllowLocalhostManagementUri"))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp || uri.Port is not (4200 or 80))
        {
            return null;
        }

        return uri.Host is "localhost" or "127.0.0.1" ? uri.AbsoluteUri : null;
    }
}
