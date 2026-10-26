namespace Demo.Performance;

/// <summary>
/// One request sent during a load run. <see cref="StatusCode"/> is 0 when no HTTP response was
/// received (a network error or timeout), in which case <see cref="Error"/> says why.
/// </summary>
public sealed record RequestSample(TimeSpan StartedAt, TimeSpan Duration, int StatusCode, string? Error = null)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
