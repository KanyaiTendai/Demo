using System.Text.Json.Serialization;

namespace Demo.Models.Orders;

/// <summary>
/// A HATEOAS link returned alongside an order resource, e.g. the buyer "approve" URL.
/// </summary>
public sealed record OrderLink
{
    [JsonPropertyName("href")]
    public string? Href { get; init; }

    [JsonPropertyName("rel")]
    public string? Rel { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }
}