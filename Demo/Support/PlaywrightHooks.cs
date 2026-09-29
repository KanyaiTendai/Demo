using Demo.Clients;
using Demo.Configuration;
using DotNetEnv;
using Reqnroll;
using Reqnroll.BoDi;

namespace Demo.Support;

[Binding]
public sealed class PlaywrightHooks
{
    private static IPlaywright? _playwright;

    private readonly IObjectContainer _objectContainer;

    public PlaywrightHooks(IObjectContainer objectContainer)
    {
        _objectContainer = objectContainer;
    }

    [BeforeTestRun]
    public static async Task BeforeTestRunAsync()
    {
        Env.TraversePath().Load();
        _playwright = await Playwright.CreateAsync();
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
        _playwright?.Dispose();
        _playwright = null;
    }

    [BeforeScenario(Order = 0)]
    public async Task BeforeScenarioAsync(ApiTestContext apiTestContext)
    {
        if (_playwright is null)
        {
            throw new InvalidOperationException($"{nameof(Playwright)} was not initialized by {nameof(BeforeTestRunAsync)}.");
        }

        var apiSettings = ApiSettings.LoadFromEnvironment();

        var apiRequestContext = await _playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = apiSettings.BaseUrl
        });

        apiTestContext.ApiRequestContext = apiRequestContext;
        apiTestContext.ClientId = apiSettings.ClientId;
        apiTestContext.ClientSecret = apiSettings.ClientSecret;

        _objectContainer.RegisterInstanceAs(apiSettings);
        _objectContainer.RegisterInstanceAs(apiRequestContext);
        _objectContainer.RegisterTypeAs<AuthenticationApiClient, IAuthenticationApiClient>();
        _objectContainer.RegisterTypeAs<OrdersApiClient, IOrdersApiClient>();
    }

    [AfterScenario]
    public async Task AfterScenarioAsync(ApiTestContext apiTestContext)
    {
        if (apiTestContext.ApiRequestContext is not null)
        {
            await apiTestContext.ApiRequestContext.DisposeAsync();
        }
    }
}
