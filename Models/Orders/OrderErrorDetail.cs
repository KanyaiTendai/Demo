using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// A single validation failure within a PayPal v2 error response's "details" array.
/// </summary>
public sealed record OrderErrorDetail
{
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("issue")]
    public string? Issue { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}