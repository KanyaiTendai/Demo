using System.Globalization;
using System.Text;

namespace Demo.Performance;

/// <summary>One request from a burst, with its raw response body for asserting on the outcome.</summary>
public sealed record BurstResponse(RequestSample Sample, string Body);

/// <summary>The outcome of a burst: every response, in the order the requests started.</summary>
public sealed class BurstResult
{
    public IReadOnlyList<BurstResponse> Responses { get; }

    public TimeSpan Duration { get; }

    /// <summary>
    /// Time between the first and last request leaving. Small values show the requests really did
    /// go out together.
    /// </summary>
    public TimeSpan StartSpread =>
        Responses.Count < 2 ? TimeSpan.Zero : Responses[^1].Sample.StartedAt - Responses[0].Sample.StartedAt;

    public BurstResult(IReadOnlyList<BurstResponse> responses, TimeSpan duration)
    {
        Responses = responses;
        Duration = duration;
    }

    public string ToSummary(string targetName)
    {
        var builder = new StringBuilder();

        builder.AppendLine(Invariant($"Burst run: {targetName}"));
        builder.AppendLine(Invariant($"Requests:      {Responses.Count} released together"));
        builder.AppendLine(Invariant($"Start spread:  {StartSpread.TotalMilliseconds:F1} ms"));
        builder.AppendLine(Invariant($"Duration:      {Duration.TotalSeconds:F2} s"));
        builder.AppendLine();
        builder.AppendLine("  #  start (ms)  duration (ms)  status  body");

        for (var i = 0; i < Responses.Count; i++)
        {
            var (sample, body) = Responses[i];
            var detail = sample.Error ?? Truncate(body, 300);
            builder.AppendLine(Invariant(
                $"{i + 1,3}  {sample.StartedAt.TotalMilliseconds,10:F1}  {sample.Duration.TotalMilliseconds,13:F0}  {sample.StatusCode,6}  {detail}"));
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..maxLength]}...";

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
