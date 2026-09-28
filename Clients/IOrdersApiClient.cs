using Demo.Models.Orders;
using Microsoft.Playwright;

namespace Demo.Clients;

/// <summary>
/// Client for PayPal's Orders v2 API (POST /v2/checkout/orders and its sub-resources).
/// </summary>
public interface IOrdersApiClient
{
    /// <summary>
    /// Creates a checkout order using the given bearer access token. Returns the raw response so
    /// callers can assert on status code and error bodies too.
    /// </summary>
    Task<IAPIResponse> CreateOrderAsync(string accessToken, CreateOrderRequest request, string? payPalRequestId = null);

    /// <summary>
    /// Authorizes an existing order (POST /v2/checkout/orders/{id}/authorize) using the given
    /// bearer access token. Returns the raw response so callers can assert on status code and
    /// error bodies too.
    /// </summary>
    Task<IAPIResponse> AuthorizeOrderAsync(string accessToken, string orderId, string? payPalRequestId = null);
}