using System.Text;
using Demo.Configuration;
using Demo.Models.Authentication;
using Demo.Support;
using Microsoft.Playwright;

namespace Demo.Clients;

public sealed class AuthenticationApiClient : IAuthenticationApiClient
{
    private const string TokenEndpoint = "/v1/oauth2/token";

    private readonly IAPIRequestContext _apiRequestContext;
    private readonly ApiSettings _apiSettings;

    public AuthenticationApiClient(IAPIRequestContext apiRequestContext, ApiSettings apiSettings)
    {
        _apiRequestContext = apiRequestContext;
        _apiSettings = apiSettings;
    }

    public async Task<IAPIResponse> RequestAccessTokenAsync(string clientId, string clientSecret)
    {
        // Only the grant type: no ignoreCache (which mints a new token on every call) and no extra
        // client metadata the tests never use.
        var form = _apiRequestContext.CreateFormData();
        form.Set("grant_type", "client_credentials");

        return await _apiRequestContext.PostAsync(TokenEndpoint, new APIRequestContextOptions
        {
            Form = form,
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = BuildBasicAuthHeader(clientId, clientSecret)
            }
        });
    }

    public async Task<string> GetOrCreateAccessTokenAsync()
    {
        if (AccessTokenStore.TryGetValidToken(out var cachedAccessToken))
        {
            return cachedAccessToken;
        }

        if (!_apiSettings.HasValidCredentials)
        {
            throw new InvalidOperationException(
                "PayPal sandbox client credentials are not configured. Set the PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET environment variables.");
        }

        var response = await RequestAccessTokenAsync(_apiSettings.ClientId, _apiSettings.ClientSecret);

        if (!response.Ok)
        {
            // Status and PayPal's error code only: this message reaches logs and the public report,
            // so the raw body is never included.
            var error = await TryReadErrorCodeAsync(response);
            throw new InvalidOperationException(
                $"Failed to obtain a PayPal access token. Status: {response.Status}. Error: {error}");
        }

        var accessTokenResponse = await response.JsonAsync<AccessTokenResponse>();

        if (string.IsNullOrWhiteSpace(accessTokenResponse?.AccessToken))
        {
            throw new InvalidOperationException("PayPal token response did not contain an access token.");
        }

        AccessTokenStore.Store(accessTokenResponse.AccessToken, accessTokenResponse.ExpiresIn);

        return accessTokenResponse.AccessToken;
    }

    private static async Task<string> TryReadErrorCodeAsync(IAPIResponse response)
    {
        try
        {
            var errorResponse = await response.JsonAsync<AccessTokenErrorResponse>();
            return errorResponse?.Error ?? "(none)";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or PlaywrightException)
        {
            return "(response was not JSON)";
        }
    }

    private static string BuildBasicAuthHeader(string clientId, string clientSecret)
    {
        var credentialBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        return $"Basic {Convert.ToBase64String(credentialBytes)}";
    }
}
