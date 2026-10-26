using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Playwright;

namespace Demo.Performance;

/// <summary>
/// Sends a request repeatedly according to a <see cref="LoadProfile"/> and records how long each
/// one took. A circuit breaker stops the run as soon as PayPal signals that it is rate limiting
/// or rejecting the client, so a run can never keep hammering an endpoint that is pushing back.
/// Requests are never retried.
/// </summary>
public static class LoadRunner
{
    public static async Task<LoadTestResult> RunAsync(
        LoadProfile profile,
        int maxConsecutiveFailures,
        Func<Task<IAPIResponse>> sendRequest)
    {
        var samples = new ConcurrentBag<RequestSample>();
        var pacer = new RequestPacer(profile.MinimumInterval);
        var circuitBreaker = new CircuitBreaker(maxConsecutiveFailures);
        using var abort = new CancellationTokenSource();
        var runClock = Stopwatch.StartNew();
        var requestsClaimed = 0;

        async Task VirtualUserAsync()
        {
            while (!abort.IsCancellationRequested && Interlocked.Increment(ref requestsClaimed) <= profile.TotalRequests)
            {
                try
                {
                    await pacer.WaitForSlotAsync(abort.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var startedAt = runClock.Elapsed;
                var requestClock = Stopwatch.StartNew();
                RequestSample sample;

                try
                {
                    var response = await sendRequest();
                    sample = new RequestSample(startedAt, requestClock.Elapsed, response.Status);
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    sample = new RequestSample(startedAt, requestClock.Elapsed, 0, ex.Message);
                }

                samples.Add(sample);

                if (circuitBreaker.Record(sample.StatusCode))
                {
                    abort.Cancel();
                }
            }
        }

        var virtualUsers = Enumerable.Range(0, profile.ConcurrentUsers).Select(_ => VirtualUserAsync());
        await Task.WhenAll(virtualUsers);
        runClock.Stop();

        return new LoadTestResult(
            profile,
            samples.OrderBy(sample => sample.StartedAt).ToList(),
            runClock.Elapsed,
            circuitBreaker.TripReason);
    }

    private sealed class CircuitBreaker
    {
        private readonly object _syncRoot = new();
        private readonly int _maxConsecutiveFailures;

        private int _consecutiveFailures;

        public string? TripReason { get; private set; }

        public CircuitBreaker(int maxConsecutiveFailures)
        {
            _maxConsecutiveFailures = maxConsecutiveFailures;
        }

        /// <summary>Records a response and returns true when the run must stop.</summary>
        public bool Record(int statusCode)
        {
            lock (_syncRoot)
            {
                if (TripReason is not null)
                {
                    return true;
                }

                TripReason = statusCode switch
                {
                    429 => "HTTP 429 Too Many Requests: PayPal is rate limiting this client.",
                    401 or 403 => $"HTTP {statusCode}: PayPal rejected valid credentials. Stopping to avoid a run of failed authentication attempts.",
                    _ => null
                };

                if (TripReason is null)
                {
                    _consecutiveFailures = statusCode is 0 or >= 500 ? _consecutiveFailures + 1 : 0;

                    if (_consecutiveFailures >= _maxConsecutiveFailures)
                    {
                        TripReason = $"{_consecutiveFailures} consecutive server or network errors.";
                    }
                }

                return TripReason is not null;
            }
        }
    }
}
