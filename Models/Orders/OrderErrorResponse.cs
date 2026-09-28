using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// Error response body returned by POST /v2/checkout/orders for a malformed or invalid request
/// (e.g. HTTP 400 INVALID_REQUEST). Bearer token errors use PayPal's OAuth error shape instead
/// (see <see cref="Demo.Models.Authentication.AccessTokenErrorResponse"/>).
/// </summary>
public sealed record OrderErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; init; }

    [JsonPropertyName("details")]
    public IReadOnlyList<OrderErrorDetail>? Details { get; init; }
}