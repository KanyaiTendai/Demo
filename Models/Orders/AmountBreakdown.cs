using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// Breakdown of a purchase unit's total amount, e.g. the sum of its item prices.
/// </summary>
public sealed record AmountBreakdown
{
    [JsonPropertyName("item_total")]
    public Money? ItemTotal { get; init; }
}