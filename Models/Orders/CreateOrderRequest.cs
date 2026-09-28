using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// Request body for POST /v2/checkout/orders.
/// </summary>
public sealed record CreateOrderRequest
{
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    [JsonPropertyName("purchase_units")]
    public required IReadOnlyList<PurchaseUnitRequest> PurchaseUnits { get; init; }

    [JsonPropertyName("application_context")]
    public OrderApplicationContext? ApplicationContext { get; init; }
}