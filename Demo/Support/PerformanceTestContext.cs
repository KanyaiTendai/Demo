using Demo.Configuration;
using Demo.Performance;
using Microsoft.Playwright;

namespace Demo.Support;

/// <summary>
/// Scenario-scoped state for the performance scenarios, shared via Reqnroll context injection
/// alongside <see cref="ApiTestContext"/>.
/// </summary>
public sealed class PerformanceTestContext
{
    public PerformanceSettings Settings { get; set; } = null!;

    public LoadProfile? LoadProfile { get; set; }

    public IReadOnlyList<string> SafetyViolations { get; set; } = [];

    public LoadTestResult? LoadTestResult { get; set; }

    public int? BurstSize { get; set; }

    /// <summary>The PayPal-Request-Id every request in the burst shares.</summary>
    public string? SharedPayPalRequestId { get; set; }

    public BurstResult? BurstResult { get; set; }

    /// <summary>The order request sent again with the shared PayPal-Request-Id after the burst.</summary>
    public IAPIResponse? ResentResponse { get; set; }
}
