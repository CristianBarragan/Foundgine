using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Npgsql;
using NpgsqlTypes;
using Foundgine.Providers.Gateway.Providers;

namespace Foundgine.Providers.EventStore;

public sealed class PostgresEventStore : IEventStore, IFoundgineProvider
{
    public string Id => "foundgine.postgres.eventstore";
    public string Name => "PostgreSQL event store";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.EventStore;
    public FoundgineProviderCapability Capabilities =>
        FoundgineProviderCapability.DurableStorage |
        FoundgineProviderCapability.OrderedEvents |
        FoundgineProviderCapability.EventReplay |
        FoundgineProviderCapability.TransactionalAppend |
        FoundgineProviderCapability.TenantIsolation;

    private readonly NpgsqlDataSource _dataSource;
    private readonly EventStoreOptions _options;

    public PostgresEventStore(NpgsqlDataSource dataSource, EventStoreOptions? options = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? new EventStoreOptions();
        _options.Validate();
    }

    public async Task<long> AppendAsync<T>(
        string tenantId, string streamId, Guid eventId, string eventType,
        DateTimeOffset occurredAt, T payload, JsonTypeInfo<T> payloadTypeInfo,
        string? correlationId = null, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(streamId, nameof(streamId));
        if (correlationId is not null) ValidateIdentity(correlationId, nameof(correlationId));
        ValidateIdentity(eventType, nameof(eventType));
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event ID must be stable and non-empty.", nameof(eventId));
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);

        var json = JsonSerializer.Serialize(payload, payloadTypeInfo);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A per-event advisory lock makes duplicate checks safe across app instances.
        await using (var eventLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction))
        {
            eventLock.Parameters.AddWithValue("lock_key", $"event:{tenantId}:{eventId:N}");
            await eventLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var existing = new NpgsqlCommand("""
            SELECT sequence FROM foundgine_events
            WHERE tenant_id = @tenant_id AND event_id = @event_id;
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("tenant_id", tenantId);
            existing.Parameters.AddWithValue("event_id", eventId);
            var value = await existing.ExecuteScalarAsync(cancellationToken);
            if (value is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // Serialize sequence allocation for this tenant/stream.
        await using (var streamLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction))
        {
            streamLock.Parameters.AddWithValue("lock_key", $"stream:{tenantId}:{streamId}");
            await streamLock.ExecuteNonQueryAsync(cancellationToken);
        }

        long sequence;
        await using (var allocate = new NpgsqlCommand("""
            INSERT INTO foundgine_event_streams (tenant_id, stream_id, last_sequence)
            VALUES (@tenant_id, @stream_id, 1)
            ON CONFLICT (tenant_id, stream_id)
            DO UPDATE SET last_sequence = foundgine_event_streams.last_sequence + 1
            RETURNING last_sequence;
            """, connection, transaction))
        {
            allocate.Parameters.AddWithValue("tenant_id", tenantId);
            allocate.Parameters.AddWithValue("stream_id", streamId);
            sequence = Convert.ToInt64(
                await allocate.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO foundgine_events
                (tenant_id, stream_id, sequence, event_id, event_type, occurred_at, payload, correlation_id)
            VALUES
                (@tenant_id, @stream_id, @sequence, @event_id, @event_type, @occurred_at, @payload, @correlation_id);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("tenant_id", tenantId);
            insert.Parameters.AddWithValue("stream_id", streamId);
            insert.Parameters.AddWithValue("sequence", sequence);
            insert.Parameters.AddWithValue("event_id", eventId);
            insert.Parameters.AddWithValue("event_type", eventType);
            insert.Parameters.AddWithValue("occurred_at", occurredAt);
            insert.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = json;
            insert.Parameters.Add("correlation_id", NpgsqlDbType.Varchar).Value = (object?)correlationId ?? DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return sequence;
    }

    public async Task<IReadOnlyList<EventEnvelope<T>>> ReadAsync<T>(
        string tenantId, string streamId, long afterSequence, int? take,
        JsonTypeInfo<T> payloadTypeInfo, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(streamId, nameof(streamId));
        if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);
        var limit = ResolveTake(take);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT event_id, tenant_id, stream_id, sequence, event_type, occurred_at, payload, correlation_id
            FROM foundgine_events
            WHERE tenant_id = @tenant_id AND stream_id = @stream_id
              AND sequence > @after_sequence
            ORDER BY sequence
            LIMIT @take;
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("stream_id", streamId);
        command.Parameters.AddWithValue("after_sequence", afterSequence);
        command.Parameters.AddWithValue("take", limit);
        return await ReadEnvelopesAsync(command, limit, payloadTypeInfo, cancellationToken);
    }

    public async Task<IReadOnlyList<EventEnvelope<T>>> ReadByCorrelationAsync<T>(
        string tenantId, string streamId, string correlationId, int take,
        JsonTypeInfo<T> payloadTypeInfo, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(streamId, nameof(streamId));
        ValidateIdentity(correlationId, nameof(correlationId));
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);
        var limit = ResolveTake(take);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        // Latest N matches, returned oldest-first (same semantics as the in-memory journal).
        await using var command = new NpgsqlCommand("""
            SELECT event_id, tenant_id, stream_id, sequence, event_type, occurred_at, payload, correlation_id
            FROM (
                SELECT * FROM foundgine_events
                WHERE tenant_id = @tenant_id AND stream_id = @stream_id AND correlation_id = @correlation_id
                ORDER BY sequence DESC
                LIMIT @take
            ) latest
            ORDER BY sequence;
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("stream_id", streamId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("take", limit);
        return await ReadEnvelopesAsync(command, limit, payloadTypeInfo, cancellationToken);
    }

    public async Task<long> GetLatestSequenceAsync(
        string tenantId, string streamId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(streamId, nameof(streamId));
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT last_sequence FROM foundgine_event_streams WHERE tenant_id = @tenant_id AND stream_id = @stream_id;",
            connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("stream_id", streamId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private int ResolveTake(int? take)
    {
        var limit = take ?? _options.DefaultReplayBatchSize;
        if (limit < 1 || limit > _options.MaximumReplayBatchSize)
            throw new ArgumentOutOfRangeException(nameof(take));
        return limit;
    }

    private static async Task<IReadOnlyList<EventEnvelope<T>>> ReadEnvelopesAsync<T>(
        NpgsqlCommand command, int limit, JsonTypeInfo<T> payloadTypeInfo, CancellationToken cancellationToken)
    {
        var events = new List<EventEnvelope<T>>(Math.Min(limit, 256));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = JsonSerializer.Deserialize(reader.GetString(6), payloadTypeInfo);
            if (payload is null) throw new JsonException("Stored event payload deserialized to null.");
            events.Add(new EventEnvelope<T>(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5), payload,
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return events;
    }

    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset olderThan, int batchSize = 1_000,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH expired AS (
                SELECT ctid FROM foundgine_events
                WHERE occurred_at < @older_than
                ORDER BY occurred_at
                LIMIT @batch_size
                FOR UPDATE SKIP LOCKED
            )
            DELETE FROM foundgine_events e USING expired
            WHERE e.ctid = expired.ctid;
            """, connection);
        command.Parameters.AddWithValue("older_than", olderThan);
        command.Parameters.AddWithValue("batch_size", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("Value must contain 1–200 non-whitespace characters.", parameterName);
    }
}
