using System.Globalization;
using Demo.Clients;
using Demo.Models.Authentication;
using Demo.Models.Orders;
using Demo.Support;
using Reqnroll;

namespace Demo.StepDefinitions;

[Binding]
public sealed class OrdersSteps
{
    private const string InvalidAccessToken = "invalid-access-token";
    private const string ReturnUrl = "https://example.com/return";
    private const string CancelUrl = "https://example.com/cancel";

    private readonly ApiTestContext _apiTestContext;
    private readonly IAuthenticationApiClient _authenticationApiClient;
    private readonly IOrdersApiClient _ordersApiClient;

    public OrdersSteps(
        ApiTestContext apiTestContext,
        IAuthenticationApiClient authenticationApiClient,
        IOrdersApiClient ordersApiClient)
    {
        _apiTestContext = apiTestContext;
        _authenticationApiClient = authenticationApiClient;
        _ordersApiClient = ordersApiClient;
    }

    [Given(@"a valid PayPal access token has been obtained")]
    public async Task GivenAValidPayPalAccessTokenHasBeenObtained()
    {
        _apiTestContext.AccessToken = await _authenticationApiClient.GetOrCreateAccessTokenAsync();
    }

    [Given(@"an invalid PayPal access token")]
    public void GivenAnInvalidPayPalAccessToken()
    {
        _apiTestContext.AccessToken = InvalidAccessToken;
    }

    [Given(@"a PayPal order has been created with intent ""(.*)""")]
    public async Task GivenAPayPalOrderHasBeenCreatedWithIntentAsync(string intent)
    {
        GivenAnOrderRequestForItemPricedAtWithIntent(1, "T-Shirt", "100.00", "USD", intent);

        var response = await _ordersApiClient.CreateOrderAsync(
            _apiTestContext.AccessToken,
            _apiTestContext.CreateOrderRequest!,
            Guid.NewGuid().ToString());

        Assert.That(response.Ok, Is.True, "Precondition failed: could not create the order to authorize.");

        _apiTestContext.CreateOrderResponse = await response.JsonAsync<CreateOrderResponse>();
    }

    [Given(@"a non-existent PayPal order id")]
    public void GivenANonExistentPayPalOrderId()
    {
        _apiTestContext.CreateOrderResponse = new CreateOrderResponse { Id = "BOGUS-ORDER-ID-123" };
    }

    [Given(@"an order request for (\d+) ""(.*)"" item priced at ""(.*)"" ""(.*)"" with intent ""(.*)""")]
    public void GivenAnOrderRequestForItemPricedAtWithIntent(
        int quantity, string itemName, string unitAmountValue, string currencyCode, string intent)
    {
        var itemTotalValue = (decimal.Parse(unitAmountValue, CultureInfo.InvariantCulture) * quantity)
            .ToString("F2", CultureInfo.InvariantCulture);

        _apiTestContext.CreateOrderRequest = new CreateOrderRequest
        {
            Intent = intent,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Items = new[]
                    {
                        new OrderItem
                        {
                            Name = itemName,
                            Description = itemName,
                            Quantity = quantity.ToString(CultureInfo.InvariantCulture),
                            UnitAmount = new Money { CurrencyCode = currencyCode, Value = unitAmountValue }
                        }
                    },
                    Amount = new PurchaseUnitAmount
                    {
                        CurrencyCode = currencyCode,
                        Value = itemTotalValue,
                        Breakdown = new AmountBreakdown
                        {
                            ItemTotal = new Money { CurrencyCode = currencyCode, Value = itemTotalValue }
                        }
                    }
                }
            },
            ApplicationContext = new OrderApplicationContext
            {
                ReturnUrl = ReturnUrl,
                CancelUrl = CancelUrl
            }
        };
    }

    [Given(@"an order request for 1 ""(.*)"" item priced at ""(.*)"" ""(.*)"" with intent ""(.*)"" but no purchase unit amount")]
    public void GivenAnOrderRequestWithNoPurchaseUnitAmount(string itemName, string unitAmountValue, string currencyCode, string intent)
    {
        _apiTestContext.CreateOrderRequest = new CreateOrderRequest
        {
            Intent = intent,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Items = new[]
                    {
                        new OrderItem
                        {
                            Name = itemName,
                            Description = itemName,
                            Quantity = "1",
                            UnitAmount = new Money { CurrencyCode = currencyCode, Value = unitAmountValue }
                        }
                    },
                    Amount = null
                }
            },
            ApplicationContext = new OrderApplicationContext
            {
                ReturnUrl = ReturnUrl,
                CancelUrl = CancelUrl
            }
        };
    }

    [When(@"I create the order")]
    public async Task WhenICreateTheOrderAsync()
    {
        Assert.That(_apiTestContext.CreateOrderRequest, Is.Not.Null);

        var response = await _ordersApiClient.CreateOrderAsync(
            _apiTestContext.AccessToken,
            _apiTestContext.CreateOrderRequest!,
            Guid.NewGuid().ToString());

        _apiTestContext.LastResponse = response;

        if (response.Ok)
        {
            _apiTestContext.CreateOrderResponse = await response.JsonAsync<CreateOrderResponse>();
        }
        else if (response.Status == 401)
        {
            _apiTestContext.OrderAuthenticationErrorResponse = await response.JsonAsync<AccessTokenErrorResponse>();
        }
        else
        {
            _apiTestContext.OrderErrorResponse = await response.JsonAsync<OrderErrorResponse>();
        }
    }

    [When(@"I authorize the order")]
    public async Task WhenIAuthorizeTheOrderAsync()
    {
        Assert.That(_apiTestContext.CreateOrderResponse, Is.Not.Null);
        var orderId = _apiTestContext.CreateOrderResponse!.Id;
        Assert.That(orderId, Is.Not.Null.And.Not.Empty);

        var response = await _ordersApiClient.AuthorizeOrderAsync(
            _apiTestContext.AccessToken,
            orderId!,
            Guid.NewGuid().ToString());

        _apiTestContext.LastResponse = response;

        if (response.Ok)
        {
            _apiTestContext.CreateOrderResponse = await response.JsonAsync<CreateOrderResponse>();
        }
        else if (response.Status == 401)
        {
            _apiTestContext.OrderAuthenticationErrorResponse = await response.JsonAsync<AccessTokenErrorResponse>();
        }
        else
        {
            _apiTestContext.OrderErrorResponse = await response.JsonAsync<OrderErrorResponse>();
        }
    }

    [Then(@"the response should contain an order id")]
    public void ThenTheResponseShouldContainAnOrderId()
    {
        Assert.That(_apiTestContext.CreateOrderResponse, Is.Not.Null);
        Assert.That(_apiTestContext.CreateOrderResponse!.Id, Is.Not.Null.And.Not.Empty);
    }

    [Then(@"the response should contain an order status of ""(.*)""")]
    public void ThenTheResponseShouldContainAnOrderStatusOf(string expectedStatus)
    {
        Assert.That(_apiTestContext.CreateOrderResponse, Is.Not.Null);
        Assert.That(_apiTestContext.CreateOrderResponse!.Status, Is.EqualTo(expectedStatus));
    }

    [Then(@"the response should contain an intent of ""(.*)""")]
    public void ThenTheResponseShouldContainAnIntentOf(string expectedIntent)
    {
        Assert.That(_apiTestContext.CreateOrderResponse, Is.Not.Null);
        Assert.That(_apiTestContext.CreateOrderResponse!.Intent, Is.EqualTo(expectedIntent));
    }

    [Then(@"the response should contain an ""(.*)"" link")]
    public void ThenTheResponseShouldContainALink(string expectedRel)
    {
        Assert.That(_apiTestContext.CreateOrderResponse, Is.Not.Null);
        Assert.That(_apiTestContext.CreateOrderResponse!.Links, Is.Not.Null);
        Assert.That(_apiTestContext.CreateOrderResponse!.Links!.Any(link => link.Rel == expectedRel), Is.True);
    }

    [Then(@"the response should contain an order authentication error")]
    public void ThenTheResponseShouldContainAnOrderAuthenticationError()
    {
        Assert.That(_apiTestContext.OrderAuthenticationErrorResponse, Is.Not.Null);
        Assert.That(_apiTestContext.OrderAuthenticationErrorResponse!.Error, Is.Not.Null.And.Not.Empty);
    }

    [Then(@"the response should contain an order validation error")]
    public void ThenTheResponseShouldContainAnOrderValidationError()
    {
        Assert.That(_apiTestContext.OrderErrorResponse, Is.Not.Null);
        Assert.That(_apiTestContext.OrderErrorResponse!.Name, Is.Not.Null.And.Not.Empty);
    }

    [Then(@"the response should contain a validation error issue of ""(.*)""")]
    public void ThenTheResponseShouldContainAValidationErrorIssueOf(string expectedIssue)
    {
        Assert.That(_apiTestContext.OrderErrorResponse, Is.Not.Null);
        Assert.That(_apiTestContext.OrderErrorResponse!.Details, Is.Not.Null);
        Assert.That(_apiTestContext.OrderErrorResponse!.Details!.Any(detail => detail.Issue == expectedIssue), Is.True);
    }
}