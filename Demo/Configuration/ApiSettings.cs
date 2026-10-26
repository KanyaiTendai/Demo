namespace Demo.Configuration;

/// <summary>
/// Environment-driven configuration for the API under test. Values are read once per test run
/// so credentials never need to be hard-coded or committed to source control.
/// </summary>
public sealed class ApiSettings
{
    private const string DefaultBaseUrl = "https://api-m.sandbox.paypal.com";

    // Credentials are only ever sent to the PayPal sandbox, or to a local mock standing in for it.
    private static readonly string[] AllowedHostSuffixes = ["sandbox.paypal.com"];
    private static readonly string[] AllowedLocalHosts = ["localhost", "127.0.0.1", "::1"];

    public string BaseUrl { get; }

    public string ClientId { get; }

    public string ClientSecret { get; }

    public bool HasValidCredentials => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    private ApiSettings(string baseUrl, string clientId, string clientSecret)
    {
        BaseUrl = baseUrl;
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    public static ApiSettings LoadFromEnvironment()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("PAYPAL_BASE_URL");
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? DefaultBaseUrl : configuredBaseUrl;
        var clientId = Environment.GetEnvironmentVariable("PAYPAL_CLIENT_ID") ?? string.Empty;
        var clientSecret = Environment.GetEnvironmentVariable("PAYPAL_CLIENT_SECRET") ?? string.Empty;

        // Fail before any request is built, so a typo or a tampered variable can never send the
        // client credentials to another host or over plain HTTP.
        if (!IsAllowedBaseUrl(baseUrl))
        {
            throw new InvalidOperationException(
                $"PAYPAL_BASE_URL '{baseUrl}' is not allowed. Use an https:// URL on *.sandbox.paypal.com, " +
                "or a localhost mock.");
        }

        return new ApiSettings(baseUrl, clientId, clientSecret);
    }

    /// <summary>
    /// True when the base URL is the PayPal sandbox over HTTPS, or a local mock (HTTP or HTTPS).
    /// Production PayPal (api-m.paypal.com), look-alike hosts and plain HTTP to a remote host are
    /// all refused.
    /// </summary>
    public static bool IsAllowedBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.Trim('[', ']');

        if (AllowedLocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && AllowedHostSuffixes.Any(suffix =>
                host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));
    }
}
