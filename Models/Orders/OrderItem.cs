using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// A single line item within a purchase unit.
/// </summary>
public sealed record OrderItem
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("quantity")]
    public required string Quantity { get; init; }

    [JsonPropertyName("unit_amount")]
    public required Money UnitAmount { get; init; }
}