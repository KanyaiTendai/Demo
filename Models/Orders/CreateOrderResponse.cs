using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// Successful response body from POST /v2/checkout/orders.
/// </summary>
public sealed record CreateOrderResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("intent")]
    public string? Intent { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("links")]
    public IReadOnlyList<OrderLink>? Links { get; init; }
}