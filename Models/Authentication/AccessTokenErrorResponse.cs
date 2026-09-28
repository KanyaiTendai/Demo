using System.Text.Json.Serialization;

namespace Demo.Models.Authentication;

/// <summary>
/// Error response body returned by POST /v1/oauth2/token for invalid client credentials.
/// </summary>
public sealed record AccessTokenErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}
