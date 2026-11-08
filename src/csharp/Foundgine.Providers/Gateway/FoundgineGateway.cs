using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Diagnostics;
using Foundgine.Providers.Gateway.Diagnostics;
using Foundgine.Providers.Gateway.Events;

namespace Foundgine.Providers.Gateway;

/// <summary>Default gateway facade backed by an immutable discovery snapshot and live event stream.</summary>
public sealed class FoundgineGateway : IFoundgineGateway
{
    private readonly FoundgineGatewayDescription _description;
    private readonly FoundgineGatewayEventHub _eventHub;

    public FoundgineGateway(
        FoundgineGatewayOptions options,
        FoundgineGatewayEventHub eventHub)
    {
        ArgumentNullException.ThrowIfNull(options);
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        _description = new FoundgineGatewayDescription(
            options.Name,
            options.ContractVersion,
            options.Capabilities.ToArray());
    }

    public FoundgineGatewayDescription Describe() => _description;

    public FoundgineGatewayCapability? FindCapability(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _description.Capabilities.FirstOrDefault(capability =>
            string.Equals(capability.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public IAsyncEnumerable<FoundgineGatewayEvent> SubscribeAsync(
        CancellationToken cancellationToken = default) =>
        _eventHub.SubscribeAsync(cancellationToken);

    public IAsyncEnumerable<FoundgineGatewayEvent> SubscribeAfterAsync(long lastSequence,
        CancellationToken cancellationToken = default) =>
        _eventHub.SubscribeAfterAsync(lastSequence, cancellationToken);
}

/// <summary>
/// In-process fan-out for instant gateway notifications. Replay is backed by the configured event store;
/// the default in-memory store is process-local and not durable. Production hosts should replace it with
/// a durable implementation that provides atomic append and ordered reads.
/// </summary>
public sealed class FoundgineGatewayEventHub : IFoundgineGatewayEventPublisher
{
    private readonly ConcurrentDictionary<Guid, Channel<FoundgineGatewayEvent>> _subscribers = new();
    private readonly IReadOnlyList<IFoundgineGatewayHook> _hooks;
    private readonly int _capacity;
    private readonly int _replayPageSize;
    private readonly IFoundgineGatewayEventStore _eventStore;

    public FoundgineGatewayEventHub(
        FoundgineGatewayOptions options,
        IEnumerable<IFoundgineGatewayHook>? hooks = null,
        IFoundgineGatewayEventStore? eventStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.SubscriberBufferCapacity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(options), "SubscriberBufferCapacity must be at least one.");
        if (options.ReplayPageSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options), "ReplayPageSize must be 1–10,000.");
        _capacity = options.SubscriberBufferCapacity;
        _replayPageSize = options.ReplayPageSize;
        _hooks = hooks?.ToArray() ?? Array.Empty<IFoundgineGatewayHook>();
        _eventStore = eventStore ?? new InMemoryFoundgineGatewayEventStore();
    }

    public async ValueTask PublishAsync(
        FoundgineGatewayEventType type,
        string subject,
        string? correlationId = null,
        string? providerId = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        cancellationToken.ThrowIfCancellationRequested();

        var started = Stopwatch.GetTimestamp();
        using var activity = FoundgineGatewayDiagnostics.StartPublishActivity(type, providerId);
        var gatewayEvent = new FoundgineGatewayEvent(
            0,
            DateTimeOffset.UtcNow,
            type,
            subject,
            correlationId,
            providerId,
            attributes is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(attributes));

        // Critical hooks (for example mandatory durable audit) must accept the event before
        // best-effort integrations or live subscribers can observe it.
        foreach (var hook in _hooks.OfType<IFoundgineGatewayCriticalHook>())
        {
            try
            {
                await hook.OnEventAsync(gatewayEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                FoundgineGatewayDiagnostics.RecordHookFailure(hook.GetType().Name, critical: true);
                activity?.SetStatus(ActivityStatusCode.Error, "Critical gateway hook rejected event.");
                activity?.AddEvent(new ActivityEvent("gateway.hook.failure", tags: new ActivityTagsCollection
                {
                    ["hook.type"] = hook.GetType().Name,
                    ["hook.critical"] = true
                }));
                throw;
            }
        }

        gatewayEvent = await _eventStore.AppendAsync(gatewayEvent, cancellationToken).ConfigureAwait(false);

        foreach (var hook in _hooks.Where(hook => hook is not IFoundgineGatewayCriticalHook))
        {
            try
            {
                await hook.OnEventAsync(gatewayEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Telemetry, cache invalidation and broker fan-out hooks are isolated from the
                // request path by default. Applications needing fail-closed delivery should
                // implement IFoundgineGatewayCriticalHook.
                FoundgineGatewayDiagnostics.RecordHookFailure(hook.GetType().Name, critical: false);
            }
        }

        foreach (var subscriber in _subscribers.Values)
        {
            if (subscriber.Writer.TryWrite(gatewayEvent))
                FoundgineGatewayDiagnostics.RecordEnqueued(type);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        FoundgineGatewayDiagnostics.RecordPublished(type, providerId, elapsedMs);
    }

    public IAsyncEnumerable<FoundgineGatewayEvent> SubscribeAsync(
        CancellationToken cancellationToken = default) => SubscribeAfterAsync(0, cancellationToken);

    /// <summary>
    /// Replays retained events after the supplied sequence (paged, unbounded), then follows the live stream.
    /// Delivery is gap-free for gap-free journals: the live subscriber is registered before replay starts, and
    /// whenever a live event arrives with a sequence beyond the next expected one (events dropped by a full buffer,
    /// or published while a long replay ran) the missing range is read back from the journal before it is delivered.
    /// A journal with intentional sequence gaps costs one extra read per gap.
    /// </summary>
    public async IAsyncEnumerable<FoundgineGatewayEvent> SubscribeAfterAsync(long lastSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (lastSequence < 0) throw new ArgumentOutOfRangeException(nameof(lastSequence));
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<FoundgineGatewayEvent>(new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        // Register before reading history so nothing published during replay is lost; duplicates are removed by sequence.
        if (!_subscribers.TryAdd(id, channel))
            throw new InvalidOperationException("Unable to register gateway event subscriber.");

        var lastDelivered = lastSequence;
        try
        {
            var latestSequence = await _eventStore.GetLatestSequenceAsync(cancellationToken).ConfigureAwait(false);
            // A cursor from a previous process-local in-memory journal can exceed the new journal's high-water mark.
            // Restart from the current journal generation instead of starving the subscription.
            if (lastDelivered > latestSequence) lastDelivered = 0;

            while (true)
            {
                var page = await _eventStore.ReadAfterAsync(lastDelivered, _replayPageSize, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var gatewayEvent in page.OrderBy(x => x.Sequence))
                {
                    if (gatewayEvent.Sequence <= lastDelivered) continue;
                    lastDelivered = gatewayEvent.Sequence;
                    yield return gatewayEvent;
                }
                if (page.Count < _replayPageSize) break;
            }

            await foreach (var live in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (live.Sequence <= lastDelivered) continue;

                // Gap: back-fill from the journal (events are appended to it before they reach subscriber channels).
                while (live.Sequence > lastDelivered + 1)
                {
                    var fill = await _eventStore.ReadAfterAsync(lastDelivered, _replayPageSize, cancellationToken)
                        .ConfigureAwait(false);
                    var progressed = false;
                    foreach (var missed in fill.OrderBy(x => x.Sequence))
                    {
                        if (missed.Sequence <= lastDelivered) continue;
                        if (missed.Sequence >= live.Sequence) break;
                        lastDelivered = missed.Sequence;
                        progressed = true;
                        yield return missed;
                    }
                    if (!progressed) break; // intentional gap or retention: continue with the live event
                }

                if (live.Sequence <= lastDelivered) continue;
                lastDelivered = live.Sequence;
                yield return live;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
