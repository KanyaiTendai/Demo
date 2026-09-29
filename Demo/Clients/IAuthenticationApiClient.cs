using Microsoft.Playwright;

namespace Demo.Clients;

/// <summary>
/// Client for PayPal's OAuth2 token endpoint (POST /v1/oauth2/token).
/// </summary>
public interface IAuthenticationApiClient
{
    /// <summary>
    /// Requests an access token using the client_credentials grant for the given credentials.
    /// Returns the raw response so callers can assert on status code and error bodies too.
    /// </summary>
    Task<IAPIResponse> RequestAccessTokenAsync(string clientId, string clientSecret);

    /// <summary>
    /// Returns a valid, cached access token for the configured sandbox credentials, requesting a
    /// new one only when there is no cached token or it is close to expiring. Endpoint clients
    /// added later should call this to obtain the Bearer token they need to authenticate.
    /// </summary>
    Task<string> GetOrCreateAccessTokenAsync();
}
