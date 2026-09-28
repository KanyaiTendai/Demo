using Demo.Clients;
using Demo.Configuration;
using Demo.Models.Authentication;
using Demo.Support;
using Reqnroll;

namespace Demo.StepDefinitions;

[Binding]
public sealed class AuthenticationSteps
{
    private const string InvalidClientId = "invalid-client-id";
    private const string InvalidClientSecret = "invalid-client-secret";

    private readonly ApiTestContext _apiTestContext;
    private readonly ApiSettings _apiSettings;
    private readonly IAuthenticationApiClient _authenticationApiClient;

    public AuthenticationSteps(
        ApiTestContext apiTestContext,
        ApiSettings apiSettings,
        IAuthenticationApiClient authenticationApiClient)
    {
        _apiTestContext = apiTestContext;
        _apiSettings = apiSettings;
        _authenticationApiClient = authenticationApiClient;
    }

    [Given(@"the PayPal sandbox API base URL is configured")]
    public void GivenThePayPalSandboxApiBaseUrlIsConfigured()
    {
        Assert.That(_apiSettings.BaseUrl, Is.Not.Null.And.Not.Empty);
        Assert.That(_apiTestContext.ApiRequestContext, Is.Not.Null);
    }

    [Given(@"valid PayPal client credentials")]
    public void GivenValidPayPalClientCredentials()
    {
        if (!_apiSettings.HasValidCredentials)
        {
            Assert.Inconclusive(
                "PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET environment variables must be set to run this scenario.");
        }

        _apiTestContext.ClientId = _apiSettings.ClientId;
        _apiTestContext.ClientSecret = _apiSettings.ClientSecret;
    }

    [Given(@"invalid PayPal client credentials")]
    public void GivenInvalidPayPalClientCredentials()
    {
        _apiTestContext.ClientId = InvalidClientId;
        _apiTestContext.ClientSecret = InvalidClientSecret;
    }

    [When(@"I request an access token using the client credentials grant type")]
    public async Task WhenIRequestAnAccessTokenUsingTheClientCredentialsGrantType()
    {
        var response = await _authenticationApiClient.RequestAccessTokenAsync(
            _apiTestContext.ClientId,
            _apiTestContext.ClientSecret);

        _apiTestContext.LastResponse = response;

        if (response.Ok)
        {
            _apiTestContext.AccessTokenResponse = await response.JsonAsync<AccessTokenResponse>();
        }
        else
        {
            _apiTestContext.AccessTokenErrorResponse = await response.JsonAsync<AccessTokenErrorResponse>();
        }
    }

    [Then(@"the response status code should be (\d+)")]
    public void ThenTheResponseStatusCodeShouldBe(int expectedStatusCode)
    {
        Assert.That(_apiTestContext.LastResponse, Is.Not.Null);
        Assert.That(_apiTestContext.LastResponse!.Status, Is.EqualTo(expectedStatusCode));
    }

    [Then(@"the response should contain a non-empty access token")]
    public void ThenTheResponseShouldContainANonEmptyAccessToken()
    {
        Assert.That(_apiTestContext.AccessTokenResponse, Is.Not.Null);
        Assert.That(_apiTestContext.AccessTokenResponse!.AccessToken, Is.Not.Null.And.Not.Empty);
    }

    [Then(@"the response should contain a token type of ""(.*)""")]
    public void ThenTheResponseShouldContainATokenTypeOf(string expectedTokenType)
    {
        Assert.That(_apiTestContext.AccessTokenResponse, Is.Not.Null);
        Assert.That(_apiTestContext.AccessTokenResponse!.TokenType, Is.EqualTo(expectedTokenType));
    }

    [Then(@"the response should contain a positive token expiry")]
    public void ThenTheResponseShouldContainAPositiveTokenExpiry()
    {
        Assert.That(_apiTestContext.AccessTokenResponse, Is.Not.Null);
        Assert.That(_apiTestContext.AccessTokenResponse!.ExpiresIn, Is.GreaterThan(0));
    }

    [Then(@"the response should contain an authentication error")]
    public void ThenTheResponseShouldContainAnAuthenticationError()
    {
        Assert.That(_apiTestContext.AccessTokenErrorResponse, Is.Not.Null);
        Assert.That(_apiTestContext.AccessTokenErrorResponse!.Error, Is.Not.Null.And.Not.Empty);
    }
}
