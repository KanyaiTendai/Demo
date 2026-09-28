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
        var form = _apiRequestContext.CreateFormData();
        form.Set("grant_type", "client_credentials");
        form.Set("ignoreCache", "true");
        form.Set("return_authn_schemes", "true");
        form.Set("return_client_metadata", "true");
        form.Set("return_unconsented_scopes", "true");

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
            var errorBody = await response.TextAsync();
            throw new InvalidOperationException(
                $"Failed to obtain a PayPal access token. Status: {response.Status}. Body: {errorBody}");
        }

        var accessTokenResponse = await response.JsonAsync<AccessTokenResponse>();

        if (string.IsNullOrWhiteSpace(accessTokenResponse?.AccessToken))
        {
            throw new InvalidOperationException("PayPal token response did not contain an access token.");
        }

        AccessTokenStore.Store(accessTokenResponse.AccessToken, accessTokenResponse.ExpiresIn);

        return accessTokenResponse.AccessToken;
    }

    private static string BuildBasicAuthHeader(string clientId, string clientSecret)
    {
        var credentialBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        return $"Basic {Convert.ToBase64String(credentialBytes)}";
    }
}
