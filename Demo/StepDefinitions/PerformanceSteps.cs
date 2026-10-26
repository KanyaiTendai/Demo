using System.Globalization;
using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Demo.Clients;
using Demo.Configuration;
using Demo.Models.Orders;
using Demo.Performance;
using Demo.Support;
using Reqnroll;

namespace Demo.StepDefinitions;

[Binding]
public sealed class PerformanceSteps
{
    private readonly ApiTestContext _apiTestContext;
    private readonly ApiSettings _apiSettings;
    private readonly PerformanceTestContext _performanceTestContext;
    private readonly IAuthenticationApiClient _authenticationApiClient;
    private readonly IOrdersApiClient _ordersApiClient;

    public PerformanceSteps(
        ApiTestContext apiTestContext,
        ApiSettings apiSettings,
        PerformanceTestContext performanceTestContext,
        IAuthenticationApiClient authenticationApiClient,
        IOrdersApiClient ordersApiClient)
    {
        _apiTestContext = apiTestContext;
        _apiSettings = apiSettings;
        _performanceTestContext = performanceTestContext;
        _authenticationApiClient = authenticationApiClient;
        _ordersApiClient = ordersApiClient;
    }

    [Given(@"the performance safety limits are loaded")]
    public void GivenThePerformanceSafetyLimitsAreLoaded()
    {
        Assert.That(
            PerformanceSettings.IsAllowedTarget(_apiSettings.BaseUrl),
            Is.True,
            $"Performance scenarios only run against the PayPal sandbox or a local mock, not '{_apiSettings.BaseUrl}'.");

        _performanceTestContext.Settings = PerformanceSettings.LoadFromEnvironment();
    }

    [Given(@"a load profile of (\d+) requests? with (\d+) concurrent users? at no more than ([0-9]+(?:\.[0-9]+)?) requests? per second")]
    public void GivenALoadProfile(int totalRequests, int concurrentUsers, string maxRequestsPerSecond)
    {
        var rate = double.Parse(maxRequestsPerSecond, CultureInfo.InvariantCulture);

        Assert.That(totalRequests, Is.GreaterThan(0), "A load profile needs at least one request.");
        Assert.That(concurrentUsers, Is.GreaterThan(0), "A load profile needs at least one concurrent user.");
        Assert.That(rate, Is.GreaterThan(0), "A load profile needs a request rate above zero.");

        _performanceTestContext.LoadProfile = new LoadProfile(totalRequests, concurrentUsers, rate);
    }

    [Given(@"a burst of (\d+) simultaneous order requests sharing one PayPal-Request-Id")]
    public void GivenABurstOfSimultaneousOrderRequestsSharingOnePayPalRequestId(int burstSize)
    {
        Assert.That(burstSize, Is.GreaterThan(1), "A burst needs at least two requests to race.");

        _performanceTestContext.BurstSize = burstSize;
        _performanceTestContext.SharedPayPalRequestId = Guid.NewGuid().ToString();
    }

    [When(@"I run the load profile against the access token endpoint")]
    public async Task WhenIRunTheLoadProfileAgainstTheAccessTokenEndpointAsync()
    {
        await RunLoadProfileAsync(
            PerformanceTarget.AccessToken,
            () => _authenticationApiClient.RequestAccessTokenAsync(_apiTestContext.ClientId, _apiTestContext.ClientSecret));
    }

    [When(@"I run the load profile against the create order endpoint")]
    public async Task WhenIRunTheLoadProfileAgainstTheCreateOrderEndpointAsync()
    {
        Assert.That(_apiTestContext.CreateOrderRequest, Is.Not.Null);
        var request = _apiTestContext.CreateOrderRequest!;

        // A fresh PayPal-Request-Id per request, so PayPal creates a new order each time instead of
        // replaying the first response from its idempotency cache.
        await RunLoadProfileAsync(
            PerformanceTarget.CreateOrder,
            () => _ordersApiClient.CreateOrderAsync(_apiTestContext.AccessToken, request, Guid.NewGuid().ToString()));
    }

    [When(@"I check the load profile against the safety limits for the create order endpoint")]
    public void WhenICheckTheLoadProfileAgainstTheSafetyLimitsForTheCreateOrderEndpoint()
    {
        Assert.That(_performanceTestContext.LoadProfile, Is.Not.Null);

        _performanceTestContext.SafetyViolations =
            _performanceTestContext.Settings.Validate(_performanceTestContext.LoadProfile!, PerformanceTarget.CreateOrder);
    }

    [When(@"I release the burst against the create order endpoint")]
    public async Task WhenIReleaseTheBurstAgainstTheCreateOrderEndpointAsync()
    {
        Assert.That(_performanceTestContext.BurstSize, Is.Not.Null);
        Assert.That(_apiTestContext.CreateOrderRequest, Is.Not.Null);

        var burstSize = _performanceTestContext.BurstSize!.Value;
        var violations = _performanceTestContext.Settings.ValidateBurst(burstSize);
        if (violations.Count > 0)
        {
            Assert.Fail($"Burst rejected before any request was sent:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        var request = _apiTestContext.CreateOrderRequest!;
        var payPalRequestId = _performanceTestContext.SharedPayPalRequestId!;

        var result = await BurstRunner.RunAsync(
            burstSize,
            () => _ordersApiClient.CreateOrderAsync(_apiTestContext.AccessToken, request, payPalRequestId));
        _performanceTestContext.BurstResult = result;

        var summary = result.ToSummary($"{PerformanceTarget.CreateOrder} (shared PayPal-Request-Id {payPalRequestId})");
        Console.WriteLine(summary);
        AllureApi.AddAttachment($"Burst summary: {PerformanceTarget.CreateOrder}", "text/plain", Encoding.UTF8.GetBytes(summary), ".txt");
    }

    [When(@"I resend the order request with the same PayPal-Request-Id after the burst")]
    public async Task WhenIResendTheOrderRequestWithTheSamePayPalRequestIdAfterTheBurstAsync()
    {
        Assert.That(_performanceTestContext.BurstResult, Is.Not.Null, "The burst must be released before the request is resent.");

        _performanceTestContext.ResentResponse = await _ordersApiClient.CreateOrderAsync(
            _apiTestContext.AccessToken,
            _apiTestContext.CreateOrderRequest!,
            _performanceTestContext.SharedPayPalRequestId!);
    }

    [When(@"I check the burst against the safety limits")]
    public void WhenICheckTheBurstAgainstTheSafetyLimits()
    {
        Assert.That(_performanceTestContext.BurstSize, Is.Not.Null);

        _performanceTestContext.SafetyViolations =
            _performanceTestContext.Settings.ValidateBurst(_performanceTestContext.BurstSize!.Value);
    }

    [Then(@"every request in the load run should have succeeded")]
    public void ThenEveryRequestInTheLoadRunShouldHaveSucceeded()
    {
        var result = GetLoadTestResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.AbortReason, Is.Null, "The circuit breaker stopped the run early.");
            Assert.That(result.RequestsSent, Is.EqualTo(result.Profile.TotalRequests), "Not every planned request was sent.");
            Assert.That(result.SuccessfulRequests, Is.EqualTo(result.RequestsSent), "Some requests did not return a 2xx status.");
        });
    }

    [Then(@"no request in the load run should have been rate limited")]
    public void ThenNoRequestInTheLoadRunShouldHaveBeenRateLimited()
    {
        Assert.That(GetLoadTestResult().RateLimitedRequests, Is.Zero);
    }

    [Then(@"the observed request rate should not have exceeded the load profile limit")]
    public void ThenTheObservedRequestRateShouldNotHaveExceededTheLoadProfileLimit()
    {
        var result = GetLoadTestResult();

        // Small tolerance for timer resolution; the pacer itself never schedules faster than the limit.
        Assert.That(result.ObservedRequestsPerSecond, Is.LessThanOrEqualTo(result.Profile.MaxRequestsPerSecond * 1.05));
    }

    [Then(@"the average response time should be below (\d+) milliseconds")]
    public void ThenTheAverageResponseTimeShouldBeBelow(int maxMilliseconds)
    {
        Assert.That(GetLoadTestResult().AverageResponseTime.TotalMilliseconds, Is.LessThan(maxMilliseconds));
    }

    [Then(@"the (\d+)(?:st|nd|rd|th) percentile response time should be below (\d+) milliseconds")]
    public void ThenThePercentileResponseTimeShouldBeBelow(int percentile, int maxMilliseconds)
    {
        Assert.That(GetLoadTestResult().Percentile(percentile).TotalMilliseconds, Is.LessThan(maxMilliseconds));
    }

    [Then(@"the requests in the burst should have been released within (\d+) milliseconds of each other")]
    public void ThenTheRequestsInTheBurstShouldHaveBeenReleasedWithinMillisecondsOfEachOther(int maxSpreadMilliseconds)
    {
        Assert.That(GetBurstResult().StartSpread.TotalMilliseconds, Is.LessThan(maxSpreadMilliseconds));
    }

    [Then(@"no request in the burst should have been rate limited")]
    public void ThenNoRequestInTheBurstShouldHaveBeenRateLimited()
    {
        Assert.That(GetBurstResult().Responses.Count(response => response.Sample.StatusCode == 429), Is.Zero);
    }

    [Then(@"exactly one order should have been created by the burst")]
    public void ThenExactlyOneOrderShouldHaveBeenCreatedByTheBurst()
    {
        Assert.That(GetBurstOrderIds().Distinct().ToList(), Has.Count.EqualTo(1),
            "Requests sharing a PayPal-Request-Id must all resolve to a single order.");
    }

    [Then(@"every request in the burst should have returned that order")]
    public void ThenEveryRequestInTheBurstShouldHaveReturnedThatOrder()
    {
        var burstResult = GetBurstResult();

        Assert.Multiple(() =>
        {
            foreach (var (sample, body) in burstResult.Responses)
            {
                Assert.That(sample.IsSuccess, Is.True, $"Burst request returned {sample.StatusCode}: {body}");
            }

            Assert.That(GetBurstOrderIds().Distinct().Count(), Is.EqualTo(1));
        });
    }

    [Then(@"the resent request should return the same order")]
    public async Task ThenTheResentRequestShouldReturnTheSameOrderAsync()
    {
        var resentResponse = _performanceTestContext.ResentResponse;
        Assert.That(resentResponse, Is.Not.Null);
        Assert.That(resentResponse!.Ok, Is.True, $"Resent request returned {resentResponse.Status}: {await resentResponse.TextAsync()}");

        var resentOrder = await resentResponse.JsonAsync<CreateOrderResponse>();
        Assert.That(resentOrder?.Id, Is.EqualTo(GetBurstOrderIds().Distinct().Single()));
    }

    [Then(@"the burst should be rejected by the safety limits")]
    public void ThenTheBurstShouldBeRejectedByTheSafetyLimits()
    {
        Assert.That(_performanceTestContext.SafetyViolations, Is.Not.Empty);
    }

    [Then(@"the load profile should be rejected by the safety limits")]
    public void ThenTheLoadProfileShouldBeRejectedByTheSafetyLimits()
    {
        Assert.That(_performanceTestContext.SafetyViolations, Is.Not.Empty);
    }

    [Then(@"no request should have been sent to PayPal")]
    public void ThenNoRequestShouldHaveBeenSentToPayPal()
    {
        Assert.That(_performanceTestContext.LoadTestResult, Is.Null);
        Assert.That(_performanceTestContext.BurstResult, Is.Null);
    }

    private async Task RunLoadProfileAsync(PerformanceTarget target, Func<Task<IAPIResponse>> sendRequest)
    {
        Assert.That(_performanceTestContext.LoadProfile, Is.Not.Null);
        var profile = _performanceTestContext.LoadProfile!;
        var settings = _performanceTestContext.Settings;

        var violations = settings.Validate(profile, target);
        if (violations.Count > 0)
        {
            Assert.Fail($"Load profile rejected before any request was sent:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        var result = await LoadRunner.RunAsync(profile, settings.MaxConsecutiveFailures, sendRequest);
        _performanceTestContext.LoadTestResult = result;

        var summary = result.ToSummary(target.ToString());
        Console.WriteLine(summary);
        AllureApi.AddAttachment($"Performance summary: {target}", "text/plain", Encoding.UTF8.GetBytes(summary), ".txt");
    }

    private BurstResult GetBurstResult()
    {
        Assert.That(_performanceTestContext.BurstResult, Is.Not.Null, "No burst has been released in this scenario.");
        return _performanceTestContext.BurstResult!;
    }

    /// <summary>The order id from every successful burst response.</summary>
    private IReadOnlyList<string?> GetBurstOrderIds() =>
        GetBurstResult().Responses
            .Where(response => response.Sample.IsSuccess)
            .Select(response => JsonSerializer.Deserialize<CreateOrderResponse>(response.Body)?.Id)
            .ToList();

    private LoadTestResult GetLoadTestResult()
    {
        Assert.That(_performanceTestContext.LoadTestResult, Is.Not.Null, "No load run has been executed in this scenario.");
        return _performanceTestContext.LoadTestResult!;
    }
}
