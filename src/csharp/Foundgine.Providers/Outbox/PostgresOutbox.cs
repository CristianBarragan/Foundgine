using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundgine.Providers.Gateway.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NpgsqlTypes;

namespace Foundgine.Providers.Outbox;

public sealed class PostgresOutboxOptions
{
    public int MaxPayloadBytes { get; set; } = 256 * 1024;
    public int MaxHeaders { get; set; } = 64;

    internal void Validate()
    {
        if (MaxPayloadBytes < 1) throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes));
        if (MaxHeaders < 0) throw new ArgumentOutOfRangeException(nameof(MaxHeaders));
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class FoundgineOutboxJsonContext : JsonSerializerContext;

/// <summary>
/// PostgreSQL outbox. <see cref="EnqueueAsync(NpgsqlTransaction, FoundgineOutboxMessage, CancellationToken)"/> writes the message
/// in the caller's own transaction, so it commits or rolls back atomically with the domain change.
/// Leasing uses <c>FOR UPDATE SKIP LOCKED</c>: any number of dispatcher instances may run concurrently.
/// Ordering across messages is NOT guaranteed.
/// </summary>
public sealed class PostgresOutbox : IFoundgineGatewayOutbox, IFoundgineGatewayOutboxStore, IFoundgineProvider
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresOutboxOptions _options;
    private readonly TimeProvider _time;

    public PostgresOutbox(NpgsqlDataSource dataSource, PostgresOutboxOptions? options = null, TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? new PostgresOutboxOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Id => "foundgine.postgres.outbox";
    public string Name => "PostgreSQL transactional outbox";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.Outbox;
    public FoundgineProviderCapability Capabilities =>
        FoundgineProviderCapability.DurableStorage |
        FoundgineProviderCapability.TransactionalOutbox |
        FoundgineProviderCapability.Retry |
        FoundgineProviderCapability.DeadLetter;

    /// <summary>Enqueues in its own transaction. Use the transaction overload for atomicity with a domain write.</summary>
    public async ValueTask EnqueueAsync(FoundgineOutboxMessage message, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await InsertAsync(connection, null, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Enqueues inside <paramref name="transaction"/>; nothing is visible to dispatchers until the caller commits.</summary>
    public async ValueTask EnqueueAsync(
        NpgsqlTransaction transaction, FoundgineOutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection
            ?? throw new InvalidOperationException("The transaction is no longer associated with a connection.");
        await InsertAsync(connection, transaction, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, FoundgineOutboxMessage message,
        CancellationToken cancellationToken)
    {
        Validate(message);
        var headers = message.Headers is { Count: > 0 }
            ? JsonSerializer.Serialize(new Dictionary<string, string>(message.Headers),
                FoundgineOutboxJsonContext.Default.DictionaryStringString)
            : "{}";
        var createdAt = message.CreatedAt ?? _time.GetUtcNow();

        await using var command = new NpgsqlCommand("""
            INSERT INTO foundgine_outbox
                (tenant_id, id, topic, message_key, payload, headers, correlation_id, created_at, available_at)
            VALUES
                (@tenant_id, @id, @topic, @message_key, @payload, @headers, @correlation_id, @created_at, @created_at)
            ON CONFLICT (tenant_id, id) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", message.TenantId);
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("topic", message.Topic);
        command.Parameters.Add("message_key", NpgsqlDbType.Varchar).Value = (object?)message.Key ?? DBNull.Value;
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = message.PayloadJson;
        command.Parameters.Add("headers", NpgsqlDbType.Jsonb).Value = headers;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Varchar).Value = (object?)message.CorrelationId ?? DBNull.Value;
        command.Parameters.AddWithValue("created_at", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<FoundgineOutboxLease>> LeaseAsync(
        int batchSize, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var token = Guid.NewGuid();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            WITH due AS (
                SELECT tenant_id, id FROM foundgine_outbox
                WHERE completed_at IS NULL AND dead_lettered_at IS NULL
                  AND available_at <= @now
                  AND (locked_until IS NULL OR locked_until <= @now)
                ORDER BY available_at, created_at
                LIMIT @batch
                FOR UPDATE SKIP LOCKED
            )
            UPDATE foundgine_outbox o
            SET locked_until = @until, lock_token = @token, attempts = o.attempts + 1
            FROM due
            WHERE o.tenant_id = due.tenant_id AND o.id = due.id
            RETURNING o.id, o.tenant_id, o.topic, o.message_key, o.payload::text, o.headers::text,
                      o.correlation_id, o.created_at, o.attempts;
            """, connection);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("until", now + leaseDuration);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("batch", batchSize);

        var leases = new List<FoundgineOutboxLease>(batchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var headers = JsonSerializer.Deserialize(
                reader.GetString(5), FoundgineOutboxJsonContext.Default.DictionaryStringString);
            var message = new FoundgineOutboxMessage(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(4),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                headers is { Count: > 0 } ? headers : null,
                reader.GetFieldValue<DateTimeOffset>(7));
            leases.Add(new FoundgineOutboxLease(message, reader.GetInt32(8), token));
        }
        return leases;
    }

    public async ValueTask<bool> CompleteAsync(
        FoundgineOutboxLease lease, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            UPDATE foundgine_outbox
            SET completed_at = @now, locked_until = NULL, lock_token = NULL
            WHERE tenant_id = @tenant_id AND id = @id AND lock_token = @token
              AND completed_at IS NULL AND dead_lettered_at IS NULL;
            """, connection);
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async ValueTask<bool> FailAsync(
        FoundgineOutboxLease lease, string error, DateTimeOffset? retryAt, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var text = error is null ? string.Empty : error.Length > 2000 ? error[..2000] : error;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(retryAt is null
            ? """
              UPDATE foundgine_outbox
              SET dead_lettered_at = @now, locked_until = NULL, lock_token = NULL, last_error = @error
              WHERE tenant_id = @tenant_id AND id = @id AND lock_token = @token
                AND completed_at IS NULL AND dead_lettered_at IS NULL;
              """
            : """
              UPDATE foundgine_outbox
              SET available_at = @retry_at, locked_until = NULL, lock_token = NULL, last_error = @error
              WHERE tenant_id = @tenant_id AND id = @id AND lock_token = @token
                AND completed_at IS NULL AND dead_lettered_at IS NULL;
              """, connection);
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("error", text);
        if (retryAt is not null) command.Parameters.AddWithValue("retry_at", retryAt.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>Retention: removes completed rows older than <paramref name="olderThan"/> in bounded batches. Dead letters are kept.</summary>
    public async Task<int> DeleteCompletedAsync(
        DateTimeOffset olderThan, int batchSize = 1_000, CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            WITH done AS (
                SELECT ctid FROM foundgine_outbox
                WHERE completed_at IS NOT NULL AND completed_at < @older_than
                ORDER BY completed_at LIMIT @batch FOR UPDATE SKIP LOCKED
            )
            DELETE FROM foundgine_outbox o USING done WHERE o.ctid = done.ctid;
            """, connection);
        command.Parameters.AddWithValue("older_than", olderThan);
        command.Parameters.AddWithValue("batch", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddLeaseParameters(NpgsqlCommand command, FoundgineOutboxLease lease)
    {
        command.Parameters.AddWithValue("tenant_id", lease.Message.TenantId);
        command.Parameters.AddWithValue("id", lease.Message.Id);
        command.Parameters.AddWithValue("token", lease.LeaseToken);
    }

    private void Validate(FoundgineOutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Id == Guid.Empty) throw new ArgumentException("Message id must be stable and non-empty.", nameof(message));
        Identity(message.TenantId, "TenantId");
        Identity(message.Topic, "Topic");
        if (message.Key is not null) Identity(message.Key, "Key");
        if (message.CorrelationId is not null) Identity(message.CorrelationId, "CorrelationId");
        if (string.IsNullOrWhiteSpace(message.PayloadJson))
            throw new ArgumentException("PayloadJson is required.", nameof(message));
        if (Encoding.UTF8.GetByteCount(message.PayloadJson) > _options.MaxPayloadBytes)
            throw new ArgumentException("PayloadJson exceeds the configured maximum size.", nameof(message));
        if (message.Headers is { Count: var count } && count > _options.MaxHeaders)
            throw new ArgumentException("Too many headers.", nameof(message));

        static void Identity(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
                throw new ArgumentException($"{name} must contain 1–200 non-whitespace characters.");
        }
    }
}

public static class FoundgineOutboxServiceCollectionExtensions
{
    /// <summary>Registers the PostgreSQL outbox as writer, dispatcher store and provider. The host supplies <see cref="NpgsqlDataSource"/>.</summary>
    public static IServiceCollection AddFoundgineGatewayPostgresOutbox(
        this IServiceCollection services, Action<PostgresOutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new PostgresOutboxOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton(options);
        services.TryAddSingleton(sp => new PostgresOutbox(
            sp.GetRequiredService<NpgsqlDataSource>(), options, sp.GetService<TimeProvider>()));
        services.TryAddSingleton<IFoundgineGatewayOutbox>(sp => sp.GetRequiredService<PostgresOutbox>());
        services.TryAddSingleton<IFoundgineGatewayOutboxStore>(sp => sp.GetRequiredService<PostgresOutbox>());
        return services.AddFoundgineProvider<PostgresOutbox>();
    }

    /// <summary>Runs the dispatcher and requires an outbox provider that declares <c>Retry</c> and <c>DeadLetter</c>.</summary>
    public static IServiceCollection AddFoundgineOutboxDispatcher<TPublisher>(
        this IServiceCollection services, Action<FoundgineOutboxDispatcherOptions>? configure = null)
        where TPublisher : class, IFoundgineOutboxPublisher
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new FoundgineOutboxDispatcherOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IFoundgineOutboxPublisher, TPublisher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FoundgineOutboxDispatcher>());
        return services.RequireFoundgineCapabilities(
            FoundgineProviderKind.Outbox,
            FoundgineProviderCapability.Retry | FoundgineProviderCapability.DeadLetter,
            "The outbox dispatcher retries and dead-letters messages.");
    }
}
