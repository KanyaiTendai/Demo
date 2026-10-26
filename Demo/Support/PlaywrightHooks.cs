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
        LoadLocalEnvFile();
        _playwright = await Playwright.CreateAsync();
    }

    /// <summary>
    /// Loads the developer's local .env file. CI always provides its values through the pipeline,
    /// so nothing is loaded there: otherwise the upward search could pick up a .env planted in a
    /// folder above the checkout. NoClobber keeps any variable that is already set, so a .env can
    /// never override a value the environment provides.
    /// </summary>
    private static void LoadLocalEnvFile()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Env.NoClobber().TraversePath().Load();
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
