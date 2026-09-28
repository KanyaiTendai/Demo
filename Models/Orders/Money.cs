using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// A currency amount, used for both item unit amounts and purchase unit totals.
/// </summary>
public sealed record Money
{
    [JsonPropertyName("currency_code")]
    public required string CurrencyCode { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}