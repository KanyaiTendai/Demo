namespace Demo.Performance;

/// <summary>
/// The shape of a load run: how many requests to send, how many may be in flight at once and the
/// highest rate they may be sent at. Defined in the feature file so it can be tuned per scenario.
/// </summary>
public sealed record LoadProfile(int TotalRequests, int ConcurrentUsers, double MaxRequestsPerSecond)
{
    /// <summary>The minimum gap between the start of one request and the start of the next.</summary>
    public TimeSpan MinimumInterval => TimeSpan.FromSeconds(1 / MaxRequestsPerSecond);
}
