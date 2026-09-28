using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// Buyer redirect URLs for the order's approval flow.
/// </summary>
public sealed record OrderApplicationContext
{
    [JsonPropertyName("return_url")]
    public required string ReturnUrl { get; init; }

    [JsonPropertyName("cancel_url")]
    public required string CancelUrl { get; init; }
}
