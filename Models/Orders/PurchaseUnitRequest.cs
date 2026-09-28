using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// A single purchase unit within a create-order request. <see cref="Amount"/> is nullable only so
/// negative test scenarios can build a request that omits it to trigger a validation error.
/// </summary>
public sealed record PurchaseUnitRequest
{
    [JsonPropertyName("items")]
    public IReadOnlyList<OrderItem>? Items { get; init; }

    [JsonPropertyName("amount")]
    public PurchaseUnitAmount? Amount { get; init; }
}