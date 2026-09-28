using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// The total amount for a purchase unit, with an optional breakdown of its components.
/// </summary>
public sealed record PurchaseUnitAmount
{
    [JsonPropertyName("currency_code")]
    public required string CurrencyCode { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("breakdown")]
    public AmountBreakdown? Breakdown { get; init; }
}