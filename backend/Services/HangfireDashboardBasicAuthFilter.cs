using System.Text;
using Hangfire.Dashboard;

namespace PriceTracker.Services;

public sealed class HangfireDashboardBasicAuthFilter : IDashboardAuthorizationFilter
{
    private readonly string _username;
    private readonly string _password;

    public HangfireDashboardBasicAuthFilter(string username, string password)
    {
        _username = username;
        _password = password;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

        if (!authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(httpContext);
            return false;
        }

        var encodedCredentials = authorizationHeader["Basic ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(encodedCredentials))
        {
            Challenge(httpContext);
            return false;
        }

        string decodedCredentials;
        try
        {
            decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        }
        catch (FormatException)
        {
            Challenge(httpContext);
            return false;
        }

        var separatorIndex = decodedCredentials.IndexOf(':');
        if (separatorIndex <= 0)
        {
            Challenge(httpContext);
            return false;
        }

        var providedUsername = decodedCredentials[..separatorIndex];
        var providedPassword = decodedCredentials[(separatorIndex + 1)..];

        if (!SecureEquals(providedUsername, _username) || !SecureEquals(providedPassword, _password))
        {
            Challenge(httpContext);
            return false;
        }

        return true;
    }

    private static bool SecureEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void Challenge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append("WWW-Authenticate", "Basic realm=\"Hangfire Dashboard\"");
    }
}
