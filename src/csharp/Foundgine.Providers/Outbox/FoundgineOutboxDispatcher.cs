using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Foundgine.Providers.Outbox;

public sealed class FoundgineOutboxDispatcherOptions
{
    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Must exceed the worst-case publish time of one batch, or messages are delivered twice.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxAttempts { get; set; } = 8;
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        if (BatchSize is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (PollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(PollInterval));
        if (LeaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(LeaseDuration));
        if (MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (BaseBackoff <= TimeSpan.Zero || MaxBackoff < BaseBackoff) throw new ArgumentOutOfRangeException(nameof(BaseBackoff));
    }
}

/// <summary>
/// Leases due messages, hands them to the publisher, then completes, schedules a retry with exponential backoff,
/// or dead-letters after <see cref="FoundgineOutboxDispatcherOptions.MaxAttempts"/>. Delivery is at-least-once.
/// </summary>
public sealed class FoundgineOutboxDispatcher : BackgroundService
{
    private readonly IFoundgineGatewayOutboxStore _store;
    private readonly IFoundgineOutboxPublisher _publisher;
    private readonly FoundgineOutboxDispatcherOptions _options;
    private readonly IFoundgineGatewayEventPublisher? _events;
    private readonly TimeProvider _time;

    public FoundgineOutboxDispatcher(
        IFoundgineGatewayOutboxStore store,
        IFoundgineOutboxPublisher publisher,
        FoundgineOutboxDispatcherOptions options,
        IFoundgineGatewayEventPublisher? events = null,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _events = events;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Deterministic exponential backoff (no jitter) for the given 1-based attempt, capped at MaxBackoff.</summary>
    public static TimeSpan ComputeBackoff(int attempt, FoundgineOutboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var exponent = Math.Clamp(attempt - 1, 0, 30);
        var millis = options.BaseBackoff.TotalMilliseconds * Math.Pow(2, exponent);
        return millis >= options.MaxBackoff.TotalMilliseconds
            ? options.MaxBackoff
            : TimeSpan.FromMilliseconds(millis);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Store outage or similar: leases expire on their own; back off and retry the loop.
                FoundgineGatewayDiagnostics.RecordOutbox("loop_error");
            }

            if (processed > 0) continue; // drain without sleeping
            try
            {
                await Task.Delay(_options.PollInterval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Runs one lease/publish/acknowledge cycle and returns the number of messages handled.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var leases = await _store.LeaseAsync(
            _options.BatchSize, _options.LeaseDuration, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        foreach (var lease in leases)
        {
            using var activity = FoundgineGatewayDiagnostics.StartOutboxActivity(lease.Message.Topic);
            Exception? failure = null;
            try
            {
                await _publisher.PublishAsync(lease.Message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // the lease simply expires and the message is redelivered
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure is null)
            {
                // A failure here propagates: the published message is redelivered after lease expiry (at-least-once).
                await _store.CompleteAsync(lease, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                FoundgineGatewayDiagnostics.RecordOutbox("published");
            }
            else
            {
                await HandleFailureAsync(lease, failure, cancellationToken).ConfigureAwait(false);
            }
        }
        return leases.Count;
    }

    private async Task HandleFailureAsync(FoundgineOutboxLease lease, Exception failure, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var error = $"{failure.GetType().Name}: {failure.Message}";
        if (lease.Attempt >= _options.MaxAttempts)
        {
            await _store.FailAsync(lease, error, null, now, cancellationToken).ConfigureAwait(false);
            FoundgineGatewayDiagnostics.RecordOutbox("dead_lettered");
            await TryAnnounceDeadLetterAsync(lease, cancellationToken).ConfigureAwait(false);
            return;
        }

        var retryAt = now + ComputeBackoff(lease.Attempt, _options);
        await _store.FailAsync(lease, error, retryAt, now, cancellationToken).ConfigureAwait(false);
        FoundgineGatewayDiagnostics.RecordOutbox("retry_scheduled");
    }

    private async ValueTask TryAnnounceDeadLetterAsync(FoundgineOutboxLease lease, CancellationToken cancellationToken)
    {
        if (_events is null) return;
        try
        {
            // Operational signal only: ids and topic, never the payload.
            await _events.PublishAsync(
                FoundgineGatewayEventType.Custom, "outbox.dead_lettered",
                correlationId: lease.Message.CorrelationId,
                attributes: new Dictionary<string, string>
                {
                    ["messageId"] = lease.Message.Id.ToString("D"),
                    ["topic"] = lease.Message.Topic,
                    ["attempts"] = lease.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The dead-letter state is already durable; announcing it is best effort.
        }
    }
}
