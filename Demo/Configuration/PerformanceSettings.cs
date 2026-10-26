using System.Globalization;
using Demo.Performance;

namespace Demo.Configuration;

/// <summary>
/// Safety ceilings for the performance scenarios. The endpoints belong to PayPal, so every load
/// profile a feature file asks for is checked against these limits before a single request is
/// sent. The defaults are deliberately conservative; each one can be raised through an
/// environment variable once more intense testing has been agreed with PayPal.
/// </summary>
public sealed class PerformanceSettings
{
    private const int DefaultMaxTotalRequests = 20;
    private const int DefaultMaxConcurrentUsers = 2;
    private const double DefaultMaxRequestsPerSecond = 1.0;
    private const int DefaultMaxTokenRequests = 5;
    private const int DefaultMaxConsecutiveFailures = 3;
    private const int DefaultMaxBurstSize = 3;

    /// <summary>Most requests a single load run may send.</summary>
    public int MaxTotalRequests { get; }

    /// <summary>Most requests a load run may have in flight at the same time.</summary>
    public int MaxConcurrentUsers { get; }

    /// <summary>Highest request rate a load run may be paced at.</summary>
    public double MaxRequestsPerSecond { get; }

    /// <summary>
    /// Most requests a load run may send to the OAuth2 token endpoint. It is kept lower than
    /// <see cref="MaxTotalRequests"/> because repeated credential exchanges are the traffic
    /// pattern most likely to be flagged by PayPal's fraud and abuse detection.
    /// </summary>
    public int MaxTokenRequests { get; }

    /// <summary>Consecutive server or network errors after which a load run is aborted.</summary>
    public int MaxConsecutiveFailures { get; }

    /// <summary>
    /// Most requests a burst may release at the same instant. Bursts bypass the request-rate
    /// pacing on purpose (a race needs simultaneous requests), so they get their own small limit.
    /// </summary>
    public int MaxBurstSize { get; }

    private PerformanceSettings(
        int maxTotalRequests,
        int maxConcurrentUsers,
        double maxRequestsPerSecond,
        int maxTokenRequests,
        int maxConsecutiveFailures,
        int maxBurstSize)
    {
        MaxTotalRequests = maxTotalRequests;
        MaxConcurrentUsers = maxConcurrentUsers;
        MaxRequestsPerSecond = maxRequestsPerSecond;
        MaxTokenRequests = maxTokenRequests;
        MaxConsecutiveFailures = maxConsecutiveFailures;
        MaxBurstSize = maxBurstSize;
    }

    public static PerformanceSettings LoadFromEnvironment() =>
        new(
            ReadPositiveInt("PERF_MAX_TOTAL_REQUESTS", DefaultMaxTotalRequests),
            ReadPositiveInt("PERF_MAX_CONCURRENT_USERS", DefaultMaxConcurrentUsers),
            ReadPositiveDouble("PERF_MAX_REQUESTS_PER_SECOND", DefaultMaxRequestsPerSecond),
            ReadPositiveInt("PERF_MAX_TOKEN_REQUESTS", DefaultMaxTokenRequests),
            ReadPositiveInt("PERF_MAX_CONSECUTIVE_FAILURES", DefaultMaxConsecutiveFailures),
            ReadPositiveInt("PERF_MAX_BURST_SIZE", DefaultMaxBurstSize));

    /// <summary>
    /// True when the base URL points at the PayPal sandbox or a local mock. Production PayPal
    /// (api-m.paypal.com) and any other host are never allowed as a load target.
    /// </summary>
    public static bool IsAllowedTarget(string baseUrl) => ApiSettings.IsAllowedBaseUrl(baseUrl);

    /// <summary>
    /// Returns every way the load profile breaks the safety limits for the given target. An empty
    /// list means the profile is safe to run.
    /// </summary>
    public IReadOnlyList<string> Validate(LoadProfile profile, PerformanceTarget target)
    {
        var violations = new List<string>();

        var maxRequests = target == PerformanceTarget.AccessToken ? MaxTokenRequests : MaxTotalRequests;
        var maxRequestsVariable = target == PerformanceTarget.AccessToken ? "PERF_MAX_TOKEN_REQUESTS" : "PERF_MAX_TOTAL_REQUESTS";

        if (profile.TotalRequests > maxRequests)
        {
            violations.Add($"{profile.TotalRequests} requests exceeds the limit of {maxRequests} for {target} ({maxRequestsVariable}).");
        }

        if (profile.ConcurrentUsers > MaxConcurrentUsers)
        {
            violations.Add($"{profile.ConcurrentUsers} concurrent users exceeds the limit of {MaxConcurrentUsers} (PERF_MAX_CONCURRENT_USERS).");
        }

        if (profile.MaxRequestsPerSecond > MaxRequestsPerSecond)
        {
            violations.Add(
                $"{profile.MaxRequestsPerSecond.ToString(CultureInfo.InvariantCulture)} requests per second exceeds the limit of " +
                $"{MaxRequestsPerSecond.ToString(CultureInfo.InvariantCulture)} (PERF_MAX_REQUESTS_PER_SECOND).");
        }

        return violations;
    }

    /// <summary>
    /// Returns every way a burst breaks the safety limits. An empty list means it is safe to send.
    /// </summary>
    public IReadOnlyList<string> ValidateBurst(int burstSize)
    {
        var violations = new List<string>();

        if (burstSize > MaxBurstSize)
        {
            violations.Add($"A burst of {burstSize} simultaneous requests exceeds the limit of {MaxBurstSize} (PERF_MAX_BURST_SIZE).");
        }

        return violations;
    }

    private static int ReadPositiveInt(string variable, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{variable} must be a positive whole number, but was '{value}'.");
        }

        return parsed;
    }

    private static double ReadPositiveDouble(string variable, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{variable} must be a positive number, but was '{value}'.");
        }

        return parsed;
    }
}
