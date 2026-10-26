using System.Diagnostics;

namespace Demo.Performance;

/// <summary>
/// Shared, thread-safe pacer that hands out request start slots no closer together than the
/// given interval. Every concurrent user waits on the same pacer, so the overall request rate
/// never goes above the profile's limit however many users there are.
/// </summary>
internal sealed class RequestPacer
{
    private readonly object _syncRoot = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan _interval;

    private TimeSpan _nextSlot = TimeSpan.Zero;

    public RequestPacer(TimeSpan interval)
    {
        _interval = interval;
    }

    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        TimeSpan slot;

        lock (_syncRoot)
        {
            var now = _clock.Elapsed;
            slot = now > _nextSlot ? now : _nextSlot;
            _nextSlot = slot + _interval;
        }

        // Task.Delay can wake a little early, so keep waiting until the slot has really been reached.
        while (_clock.Elapsed < slot)
        {
            await Task.Delay(slot - _clock.Elapsed, cancellationToken);
        }
    }
}
