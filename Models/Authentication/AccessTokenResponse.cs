using System.Text.Json.Serialization;

namespace Demo.Models.Authentication;

/// <summary>
/// Successful response body from POST /v1/oauth2/token.
/// </summary>
public sealed record AccessTokenResponse
{
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; init; }
}
