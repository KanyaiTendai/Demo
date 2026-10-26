using System.Globalization;
using System.Text;

namespace Demo.Performance;

/// <summary>
/// The outcome of a load run: every request sample plus the statistics the feature files assert on.
/// </summary>
public sealed class LoadTestResult
{
    public LoadProfile Profile { get; }

    public IReadOnlyList<RequestSample> Samples { get; }

    public TimeSpan Duration { get; }

    /// <summary>Why the circuit breaker stopped the run early, or null if it ran to completion.</summary>
    public string? AbortReason { get; }

    public int RequestsSent => Samples.Count;

    public int SuccessfulRequests => Samples.Count(sample => sample.IsSuccess);

    public int RateLimitedRequests => Samples.Count(sample => sample.StatusCode == 429);

    public double ErrorRatePercent => RequestsSent == 0 ? 0 : 100.0 * (RequestsSent - SuccessfulRequests) / RequestsSent;

    public TimeSpan MinimumResponseTime => RequestsSent == 0 ? TimeSpan.Zero : Samples.Min(sample => sample.Duration);

    public TimeSpan MaximumResponseTime => RequestsSent == 0 ? TimeSpan.Zero : Samples.Max(sample => sample.Duration);

    public TimeSpan AverageResponseTime =>
        RequestsSent == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)Samples.Average(sample => sample.Duration.Ticks));

    /// <summary>
    /// The request rate actually achieved, measured between the first and last request start.
    /// </summary>
    public double ObservedRequestsPerSecond
    {
        get
        {
            if (RequestsSent < 2)
            {
                return 0;
            }

            var span = Samples[^1].StartedAt - Samples[0].StartedAt;
            return span <= TimeSpan.Zero ? double.PositiveInfinity : (RequestsSent - 1) / span.TotalSeconds;
        }
    }

    public LoadTestResult(LoadProfile profile, IReadOnlyList<RequestSample> samples, TimeSpan duration, string? abortReason)
    {
        Profile = profile;
        Samples = samples;
        Duration = duration;
        AbortReason = abortReason;
    }

    /// <summary>Response time at the given percentile (nearest-rank method).</summary>
    public TimeSpan Percentile(int percentile)
    {
        if (RequestsSent == 0)
        {
            return TimeSpan.Zero;
        }

        var ordered = Samples.Select(sample => sample.Duration).Order().ToList();
        var rank = (int)Math.Ceiling(percentile / 100.0 * ordered.Count);
        return ordered[Math.Clamp(rank, 1, ordered.Count) - 1];
    }

    public string ToSummary(string targetName)
    {
        var builder = new StringBuilder();

        builder.AppendLine(Invariant($"Performance run: {targetName}"));
        builder.AppendLine(Invariant(
            $"Profile:       {Profile.TotalRequests} requests, {Profile.ConcurrentUsers} concurrent users, max {Profile.MaxRequestsPerSecond} req/s"));
        builder.AppendLine(Invariant($"Outcome:       {AbortReason ?? "completed"}"));
        builder.AppendLine(Invariant($"Duration:      {Duration.TotalSeconds:F2} s"));
        builder.AppendLine(Invariant($"Requests sent: {RequestsSent} ({SuccessfulRequests} succeeded, {RateLimitedRequests} rate limited)"));
        builder.AppendLine(Invariant($"Error rate:    {ErrorRatePercent:F1} %"));
        builder.AppendLine(Invariant($"Observed rate: {ObservedRequestsPerSecond:F2} req/s"));
        builder.AppendLine(Invariant(
            $"Response time: min {MinimumResponseTime.TotalMilliseconds:F0} ms, avg {AverageResponseTime.TotalMilliseconds:F0} ms, p50 {Percentile(50).TotalMilliseconds:F0} ms, p90 {Percentile(90).TotalMilliseconds:F0} ms, p95 {Percentile(95).TotalMilliseconds:F0} ms, max {MaximumResponseTime.TotalMilliseconds:F0} ms"));
        builder.AppendLine();
        builder.AppendLine("  #  start (s)  duration (ms)  status");

        for (var i = 0; i < Samples.Count; i++)
        {
            var sample = Samples[i];
            builder.AppendLine(Invariant(
                $"{i + 1,3}  {sample.StartedAt.TotalSeconds,9:F2}  {sample.Duration.TotalMilliseconds,13:F0}  {sample.StatusCode}{(sample.Error is null ? string.Empty : $" ({sample.Error})")}"));
        }

        return builder.ToString();
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
