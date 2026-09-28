using System.Text.Json;
using Demo.Models.Orders;
using Microsoft.Playwright;

namespace Demo.Clients;

public sealed class OrdersApiClient : IOrdersApiClient
{
    private const string OrdersBasePath = "/v2/checkout/orders";

    private static readonly JsonSerializerOptions RequestSerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IAPIRequestContext _apiRequestContext;

    public OrdersApiClient(IAPIRequestContext apiRequestContext)
    {
        _apiRequestContext = apiRequestContext;
    }

    public async Task<IAPIResponse> CreateOrderAsync(string accessToken, CreateOrderRequest request, string? payPalRequestId = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["Authorization"] = $"Bearer {accessToken}",
            ["Prefer"] = "return=representation"
        };

        if (!string.IsNullOrWhiteSpace(payPalRequestId))
        {
            headers["PayPal-Request-Id"] = payPalRequestId;
        }

        return await _apiRequestContext.PostAsync(OrdersBasePath, new APIRequestContextOptions
        {
            Data = JsonSerializer.Serialize(request, RequestSerializerOptions),
            Headers = headers
        });
    }

    public async Task<IAPIResponse> AuthorizeOrderAsync(string accessToken, string orderId, string? payPalRequestId = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["Authorization"] = $"Bearer {accessToken}",
            ["Prefer"] = "return=representation"
        };

        if (!string.IsNullOrWhiteSpace(payPalRequestId))
        {
            headers["PayPal-Request-Id"] = payPalRequestId;
        }

        return await _apiRequestContext.PostAsync($"{OrdersBasePath}/{orderId}/authorize", new APIRequestContextOptions
        {
            Data = string.Empty,
            Headers = headers
        });
    }
}