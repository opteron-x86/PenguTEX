namespace PenguTEX.Imaging;

/// <summary>
/// Bounds the decoded pixels held by concurrent rebuilds. Small textures share the budget; a texture
/// as large as the whole budget runs alone. Requests are served in arrival order, so a large request
/// cannot be starved by a stream of small ones.
/// </summary>
public sealed class PixelBudget(long capacity)
{
    private readonly object _gate = new();
    private readonly SortedSet<long> _waiting = [];
    private long _used, _nextTicket;

    public long Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public long InUse { get { lock (_gate) return _used; } }
    public int Waiting { get { lock (_gate) return _waiting.Count; } }

    public IDisposable Reserve(long pixels, CancellationToken ct = default)
    {
        if (pixels < 1 || pixels > Capacity) throw new ArgumentOutOfRangeException(nameof(pixels));
        // Wake waiters on cancellation. Registered and disposed outside the lock: disposal waits for a
        // running callback, which itself needs the lock.
        using var wake = ct.Register(() => { lock (_gate) Monitor.PulseAll(_gate); });
        lock (_gate)
        {
            long ticket = _nextTicket++; _waiting.Add(ticket);
            try
            {
                while (_waiting.Min != ticket || _used + pixels > Capacity)
                {
                    ct.ThrowIfCancellationRequested();
                    Monitor.Wait(_gate);
                }
                ct.ThrowIfCancellationRequested();
                _used += pixels;
            }
            finally { _waiting.Remove(ticket); Monitor.PulseAll(_gate); }
        }
        return new Lease(this, pixels);
    }

    private sealed class Lease(PixelBudget owner, long pixels) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            lock (owner._gate) { owner._used -= pixels; Monitor.PulseAll(owner._gate); }
        }
    }
}
