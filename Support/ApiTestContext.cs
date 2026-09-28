using Demo.Models.Authentication;
using Demo.Models.Orders;
using Microsoft.Playwright;

namespace Demo.Support;

/// <summary>
/// Scenario-scoped state shared across step definition classes via Reqnroll context injection.
/// A new instance is created per scenario and disposed automatically at the end of it.
/// </summary>
public sealed class ApiTestContext
{
    public IAPIRequestContext ApiRequestContext { get; set; } = null!;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public IAPIResponse? LastResponse { get; set; }

    public AccessTokenResponse? AccessTokenResponse { get; set; }

    public AccessTokenErrorResponse? AccessTokenErrorResponse { get; set; }

    public CreateOrderRequest? CreateOrderRequest { get; set; }

    public CreateOrderResponse? CreateOrderResponse { get; set; }

    public OrderErrorResponse? OrderErrorResponse { get; set; }

    public AccessTokenErrorResponse? OrderAuthenticationErrorResponse { get; set; }
}
