using System.Threading.Channels;

namespace Foundgine.Runtime.ControlPlane.AuditLog;

/// <summary>
/// Bounded, single-writer audit handoff. RecordAsync returns only after the durable sink
/// acknowledges the event. A full queue applies backpressure; events are never silently dropped.
/// Query is a process-local view of events acknowledged during this process lifetime.
/// </summary>
public sealed record AuditBufferMetrics(int QueueDepth, long Accepted, long Persisted, long Failed, long BackpressureWaits, long BackpressureMilliseconds);

public sealed class BufferedDurableAuditLog : IAuditLog, IAsyncDisposable
{
    private sealed record Pending(AuditEvent Event, TaskCompletionSource Acknowledged);

    private readonly IAuditEventStore _store;
    private readonly Channel<Pending> _queue;
    private readonly Task _worker;
    private readonly object _snapshotGate = new();
    private readonly List<AuditEvent> _committed = [];
    private readonly int _capacity;
    private int _disposed;
    private long _accepted;
    private long _persisted;
    private long _failed;
    private long _backpressureWaits;
    private long _backpressureMilliseconds;

    /// <summary>Point-in-time operational counters. Queue depth is approximate under concurrency.</summary>
    public AuditBufferMetrics Metrics => new(
        _queue.Reader.CanCount ? _queue.Reader.Count : -1,
        Interlocked.Read(ref _accepted), Interlocked.Read(ref _persisted),
        Interlocked.Read(ref _failed), Interlocked.Read(ref _backpressureWaits),
        Interlocked.Read(ref _backpressureMilliseconds));

    public BufferedDurableAuditLog(IAuditEventStore store, int capacity = 1024)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public void Record(AuditEvent auditEvent) => RecordAsync(auditEvent).AsTask().GetAwaiter().GetResult();

    public async ValueTask RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var wasFull = _queue.Reader.CanCount && _queue.Reader.Count >= _capacity;
        await _queue.Writer.WriteAsync(new Pending(auditEvent, acknowledged), cancellationToken).ConfigureAwait(false);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        if (wasFull || elapsed >= TimeSpan.FromMilliseconds(1))
        {
            Interlocked.Increment(ref _backpressureWaits);
            Interlocked.Add(ref _backpressureMilliseconds, (long)elapsed.TotalMilliseconds);
        }
        Interlocked.Increment(ref _accepted);
        // Cancellation after enqueue is deliberately not observed: the caller must learn
        // whether the durable append succeeded, rather than abandon an ambiguous write.
        await acknowledged.Task.ConfigureAwait(false);
    }

    public IReadOnlyList<AuditEvent> Query(string? toolName = null, string? tenant = null)
    {
        lock (_snapshotGate)
        {
            IEnumerable<AuditEvent> query = _committed;
            if (!string.IsNullOrWhiteSpace(toolName)) query = query.Where(e => e.ToolName == toolName);
            if (!string.IsNullOrWhiteSpace(tenant)) query = query.Where(e => e.Tenant == tenant);
            return query.ToArray();
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var pending in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _store.AppendAsync(pending.Event).ConfigureAwait(false);
                lock (_snapshotGate) _committed.Add(pending.Event);
                Interlocked.Increment(ref _persisted);
                pending.Acknowledged.TrySetResult();
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _failed);
                pending.Acknowledged.TrySetException(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false); // drains all accepted writes before returning
    }
}
