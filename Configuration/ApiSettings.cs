namespace Demo.Configuration;

/// <summary>
/// Environment-driven configuration for the API under test. Values are read once per test run
/// so credentials never need to be hard-coded or committed to source control.
/// </summary>
public sealed class ApiSettings
{
    private const string DefaultBaseUrl = "https://api-m.sandbox.paypal.com";

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
        var baseUrl = Environment.GetEnvironmentVariable("PAYPAL_BASE_URL");
        var clientId = Environment.GetEnvironmentVariable("PAYPAL_CLIENT_ID") ?? string.Empty;
        var clientSecret = Environment.GetEnvironmentVariable("PAYPAL_CLIENT_SECRET") ?? string.Empty;

        return new ApiSettings(
            string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl,
            clientId,
            clientSecret);
    }
}
