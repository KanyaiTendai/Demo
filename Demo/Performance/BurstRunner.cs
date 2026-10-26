using System.Diagnostics;
using Microsoft.Playwright;

namespace Demo.Performance;

/// <summary>
/// Releases a small number of requests at the same instant, to create the contention a race
/// condition needs. Every request waits on one start signal, so they all leave together instead
/// of being spread out by the <see cref="RequestPacer"/>. Requests are never retried.
/// </summary>
public static class BurstRunner
{
    public static async Task<BurstResult> RunAsync(int burstSize, Func<Task<IAPIResponse>> sendRequest)
    {
        // RunContinuationsAsynchronously resumes every waiting request on its own thread-pool
        // thread, so they all start sending at once rather than one after another.
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var burstClock = Stopwatch.StartNew();

        async Task<BurstResponse> SendWhenReleasedAsync()
        {
            await startSignal.Task;

            var startedAt = burstClock.Elapsed;
            var requestClock = Stopwatch.StartNew();

            try
            {
                var response = await sendRequest();
                var duration = requestClock.Elapsed;
                return new BurstResponse(new RequestSample(startedAt, duration, response.Status), await response.TextAsync());
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                return new BurstResponse(new RequestSample(startedAt, requestClock.Elapsed, 0, ex.Message), string.Empty);
            }
        }

        var requests = Enumerable.Range(0, burstSize).Select(_ => SendWhenReleasedAsync()).ToList();
        startSignal.SetResult();
        var responses = await Task.WhenAll(requests);
        burstClock.Stop();

        return new BurstResult(responses.OrderBy(response => response.Sample.StartedAt).ToList(), burstClock.Elapsed);
    }
}
