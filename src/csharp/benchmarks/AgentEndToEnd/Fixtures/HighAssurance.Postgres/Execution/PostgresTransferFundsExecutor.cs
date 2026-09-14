using Foundgine.HighAssurance.Banking;
using Foundgine.Core.Semantic.Authorization;
using Npgsql;
using System.Data;

namespace Foundgine.HighAssurance.Postgres.Execution;

/// <summary>
/// PostgreSQL execution adapter for the high-assurance TransferFunds mutation.
/// Supports both single-transfer execution and one-transaction array batching.
/// </summary>
public enum PostgresTransferFundsFaultPoint
{
    AfterMutationBeforeCommit,
    BeforeAuthorizationCommitCheck,
    AfterBatchMutationBeforeCommit,
    BeforeBatchAuthorizationCommitCheck
}

public sealed class PostgresTransferFundsExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Func<Guid, BankAccount, BankAccount, AuthorizationDecision> _authorize;
    private readonly Action<PostgresTransferFundsFaultPoint>? _faultInjector;
    private readonly PostgresAuthorizationContextStore? _authorizationContextStore;

    /// <summary>
    /// Compatibility constructor for boolean authorization callbacks. The callback is
    /// still re-evaluated at the commit gate, while the returned evidence uses a
    /// deterministic compatibility fingerprint.
    /// </summary>
    public PostgresTransferFundsExecutor(
        NpgsqlDataSource dataSource,
        Func<Guid, BankAccount, BankAccount, bool> authorize,
        Action<PostgresTransferFundsFaultPoint>? faultInjector = null,
        PostgresAuthorizationContextStore? authorizationContextStore = null)
        : this(
            dataSource,
            (actorId, source, destination) =>
                AuthorizationDecision.FromBoolean(
                    authorize(actorId, source, destination),
                    actorId,
                    source,
                    destination),
            faultInjector,
            authorizationContextStore)
    {
    }

    /// <summary>
    /// Strong authorization contract. The decision carries explicit versioned
    /// authorization evidence which is re-evaluated and compared immediately
    /// before commit.
    /// </summary>
    public PostgresTransferFundsExecutor(
        NpgsqlDataSource dataSource,
        Func<Guid, BankAccount, BankAccount, AuthorizationDecision> authorize,
        Action<PostgresTransferFundsFaultPoint>? faultInjector = null,
        PostgresAuthorizationContextStore? authorizationContextStore = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
        _faultInjector = faultInjector;
        _authorizationContextStore = authorizationContextStore;
    }

    public async Task<PostgresTransferFundsExecutionResult> ExecuteAsync(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await AdvisoryLockAsync(connection, transaction, command.IdempotencyKey, cancellationToken);
            var existing =
                await FindIdempotencyAsync(connection, transaction, command.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                EnsureReplayMatches(existing, actorId, tenantId, command);
                await transaction.CommitAsync(cancellationToken);
                return new(existing.TransferId, existing.SourceBalance, existing.DestinationBalance, true);
            }

            var accounts = await LoadAccountsForUpdateAsync(connection, transaction,
                [command.SourceAccountId, command.DestinationAccountId], cancellationToken);
            if (!accounts.TryGetValue(command.SourceAccountId, out var source))
                throw new InvalidOperationException("Source account does not exist at execution time.");
            if (!accounts.TryGetValue(command.DestinationAccountId, out var destination))
                throw new InvalidOperationException("Destination account does not exist at execution time.");
            ValidateExecution(actorId, tenantId, source, destination, command.Amount);
            var authorizationContext = await LoadAuthorizationContextForUpdateAsync(
                connection, transaction, actorId, tenantId, cancellationToken);
            var authorization = _authorize(actorId, source, destination);
            EnsureAuthorized(authorization);
            var executionBinding = AuthorizationExecutionBinding.Create(actorId, tenantId, command, authorization);
            EnsureAuthorizationEvidenceMatches(
                authorization,
                _authorize(actorId, source, destination));
            executionBinding.ValidateAgainst(actorId, tenantId, command, authorization);
            EnsureAuthorizationEvidenceMatchesStore(authorization, authorizationContext,
                _authorizationContextStore is not null);

            var transferId = Guid.NewGuid();
            var sourceBalance = source.Balance - command.Amount;
            var destinationBalance = destination.Balance + command.Amount;
            await ApplyMutationCteAsync(connection, transaction, source, destination, command, actorId, tenantId,
                transferId, sourceBalance, destinationBalance, cancellationToken);
            _faultInjector?.Invoke(PostgresTransferFundsFaultPoint.AfterMutationBeforeCommit);
            _faultInjector?.Invoke(PostgresTransferFundsFaultPoint.BeforeAuthorizationCommitCheck);
            var commitAuthorization = _authorize(actorId, source, destination);
            EnsureAuthorizationEvidenceMatches(authorization, commitAuthorization);
            executionBinding.ValidateAgainst(actorId, tenantId, command, commitAuthorization);
            var currentAuthorizationContext = await LoadAuthorizationContextForUpdateAsync(
                connection, transaction, actorId, tenantId, cancellationToken);
            EnsureAuthorizationEvidenceMatchesStore(authorization, currentAuthorizationContext,
                _authorizationContextStore is not null);
            await transaction.CommitAsync(cancellationToken);
            return new(transferId, sourceBalance, destinationBalance, false, authorization.Version,
                authorization.Fingerprint);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<PostgresTransferFundsExecutionResult>> ExecuteBatchAsync(
        Guid actorId,
        int tenantId,
        IReadOnlyList<TransferFundsCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (commands is null || commands.Count == 0)
            throw new ArgumentException("A transfer batch must contain at least one command.", nameof(commands));
        foreach (var command in commands) ValidateShape(command);
        if (commands.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != commands.Count)
            throw new InvalidOperationException("A transfer batch cannot contain duplicate idempotency keys.");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var prepared = await PrepareBatchAsync(connection, transaction, commands, cancellationToken);
            var results = new PostgresTransferFundsExecutionResult[commands.Count];
            var newIndexes = new List<int>(commands.Count);

            for (var i = 0; i < commands.Count; i++)
            {
                if (prepared.Existing.TryGetValue(commands[i].IdempotencyKey, out var existing))
                {
                    EnsureReplayMatches(existing, actorId, tenantId, commands[i]);
                    results[i] = new(existing.TransferId, existing.SourceBalance, existing.DestinationBalance, true);
                }
                else
                {
                    newIndexes.Add(i);
                }
            }

            if (newIndexes.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return results;
            }

            var authorizationContext = await LoadAuthorizationContextForUpdateAsync(
                connection, transaction, actorId, tenantId, cancellationToken);
            var outgoing = new Dictionary<Guid, decimal>();
            var incoming = new Dictionary<Guid, decimal>();
            var transferIds = new Guid[newIndexes.Count];
            var authorizations = new AuthorizationDecision[newIndexes.Count];

            var batchOutgoing = new Dictionary<Guid, decimal>();
            for (var j = 0; j < newIndexes.Count; j++)
            {
                var command = commands[newIndexes[j]];
                batchOutgoing.TryGetValue(command.SourceAccountId, out var current);
                batchOutgoing[command.SourceAccountId] = current + command.Amount;
            }

            foreach (var (accountId, batchAmount) in batchOutgoing)
            {
                var source = prepared.Accounts[accountId];
                if (source.DailyTransferred + batchAmount > source.DailyLimit)
                    throw new InvalidOperationException("Transfer batch exceeds the source account daily limit.");
                var available = source.Balance - source.PendingTransactions - source.RegulatoryHold;
                if (available < batchAmount)
                    throw new InvalidOperationException("Transfer batch exceeds the source account available funds.");
            }

            for (var j = 0; j < newIndexes.Count; j++)
            {
                var index = newIndexes[j];
                var command = commands[index];
                var source = prepared.Accounts[command.SourceAccountId];
                var destination = prepared.Accounts[command.DestinationAccountId];
                ValidateExecution(actorId, tenantId, source, destination, command.Amount);

                var authorization = _authorize(actorId, source, destination);
                EnsureAuthorized(authorization);
                EnsureAuthorizationEvidenceMatches(
                    authorization,
                    _authorize(actorId, source, destination));
                EnsureAuthorizationEvidenceMatchesStore(authorization, authorizationContext,
                    _authorizationContextStore is not null);
                authorizations[j] = authorization;

                outgoing[command.SourceAccountId] =
                    outgoing.GetValueOrDefault(command.SourceAccountId) + command.Amount;
                incoming[command.DestinationAccountId] =
                    incoming.GetValueOrDefault(command.DestinationAccountId) + command.Amount;
                transferIds[j] = Guid.NewGuid();
            }

            // Re-check aggregate invariants against the locked snapshot. This makes a batch
            // equivalent to applying all independent transfers while holding all involved rows.
            foreach (var account in prepared.Accounts.Values)
            {
                var outAmount = outgoing.GetValueOrDefault(account.Id);
                if (account.DailyTransferred + outAmount > account.DailyLimit)
                    throw new InvalidOperationException("Transfer exceeds the source account daily limit.");
                var available = account.Balance - account.PendingTransactions - account.RegulatoryHold - outAmount;
                if (available < 0)
                    throw new InvalidOperationException("Insufficient available funds.");
            }

            var sources = new Guid[newIndexes.Count];
            var destinations = new Guid[newIndexes.Count];
            var amounts = new decimal[newIndexes.Count];
            var keys = new string[newIndexes.Count];
            var transferIdArray = new Guid[newIndexes.Count];
            var sourceBalances = new decimal[newIndexes.Count];
            var destinationBalances = new decimal[newIndexes.Count];

            for (var j = 0; j < newIndexes.Count; j++)
            {
                var command = commands[newIndexes[j]];
                var source = prepared.Accounts[command.SourceAccountId];
                var destination = prepared.Accounts[command.DestinationAccountId];
                sources[j] = source.Id;
                destinations[j] = destination.Id;
                amounts[j] = command.Amount;
                keys[j] = command.IdempotencyKey;
                transferIdArray[j] = transferIds[j];
                sourceBalances[j] = source.Balance - outgoing[source.Id] + incoming.GetValueOrDefault(source.Id);
                destinationBalances[j] = destination.Balance - outgoing.GetValueOrDefault(destination.Id) +
                                         incoming.GetValueOrDefault(destination.Id);
            }

            for (var j = 0; j < newIndexes.Count; j++)
            {
                var index = newIndexes[j];
                var command = commands[index];
                var source = prepared.Accounts[command.SourceAccountId];
                var destination = prepared.Accounts[command.DestinationAccountId];
                var rechecked = _authorize(actorId, source, destination);
                EnsureAuthorizationEvidenceMatches(authorizations[j], rechecked);
                AuthorizationExecutionBinding.Create(actorId, tenantId, command, authorizations[j])
                    .ValidateAgainst(actorId, tenantId, command, rechecked);
            }

            await ApplyBatchMutationCteAsync(connection, transaction, actorId, tenantId,
                sources, destinations, amounts, keys, transferIdArray, sourceBalances, destinationBalances,
                cancellationToken);
            _faultInjector?.Invoke(PostgresTransferFundsFaultPoint.AfterBatchMutationBeforeCommit);
            _faultInjector?.Invoke(PostgresTransferFundsFaultPoint.BeforeBatchAuthorizationCommitCheck);

            var currentAuthorizationContext = await LoadAuthorizationContextForUpdateAsync(
                connection, transaction, actorId, tenantId, cancellationToken);
            for (var j = 0; j < newIndexes.Count; j++)
            {
                var index = newIndexes[j];
                var command = commands[index];
                var source = prepared.Accounts[command.SourceAccountId];
                var destination = prepared.Accounts[command.DestinationAccountId];
                var commitAuthorization = _authorize(actorId, source, destination);
                EnsureAuthorizationEvidenceMatches(authorizations[j], commitAuthorization);
                AuthorizationExecutionBinding.Create(actorId, tenantId, command, authorizations[j])
                    .ValidateAgainst(actorId, tenantId, command, commitAuthorization);
                EnsureAuthorizationEvidenceMatchesStore(authorizations[j], currentAuthorizationContext,
                    _authorizationContextStore is not null);
                results[index] = new(transferIds[j], sourceBalances[j], destinationBalances[j], false,
                    authorizations[j].Version, authorizations[j].Fingerprint);
            }

            await transaction.CommitAsync(cancellationToken);
            return results;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private sealed record PreparedBatch(
        Dictionary<Guid, BankAccount> Accounts,
        Dictionary<string, IdempotencyRow> Existing);

    private static async Task<PreparedBatch> PrepareBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<TransferFundsCommand> commands,
        CancellationToken ct)
    {
        var sourceIds = commands.Select(x => x.SourceAccountId).ToArray();
        var destinationIds = commands.Select(x => x.DestinationAccountId).ToArray();
        var keys = commands.Select(x => x.IdempotencyKey).ToArray();

        const string lockSql = """
                               WITH input AS (
                                   SELECT *
                                   FROM unnest(
                                       @source_ids::uuid[],
                                       @destination_ids::uuid[],
                                       @keys::text[]
                                   ) AS t(source_id, destination_id, idempotency_key)
                               ),
                               lock_keys AS MATERIALIZED (
                                   SELECT idempotency_key,
                                          pg_advisory_xact_lock(hashtextextended(idempotency_key, 0))
                                   FROM input
                                   ORDER BY idempotency_key
                               ),
                               account_ids AS (
                                   SELECT source_id AS id FROM input
                                   UNION
                                   SELECT destination_id FROM input
                               ),
                               locked_accounts AS MATERIALIZED (
                                   SELECT a.*
                                   FROM banking.bank_account a
                                   JOIN account_ids ids ON ids.id = a.id
                                   ORDER BY a.id
                                   FOR UPDATE
                               )
                               SELECT a.id, a.tenant_id, a.owner_id, a.balance, a.pending_transactions,
                                      a.regulatory_hold, a.daily_transferred, a.daily_limit, a.is_frozen
                               FROM locked_accounts a
                               CROSS JOIN (SELECT count(*) AS locked FROM lock_keys) lk
                               ORDER BY a.id;

                               SELECT actor_id, tenant_id, source_account_id, destination_account_id, amount,
                                      transfer_id, source_balance, destination_balance, idempotency_key
                               FROM banking.transfer_idempotency
                               WHERE idempotency_key = ANY(@keys::text[])
                               ORDER BY idempotency_key;
                               """;

        await using var command = new NpgsqlCommand(lockSql, connection, transaction);
        command.Parameters.AddWithValue("source_ids", sourceIds);
        command.Parameters.AddWithValue("destination_ids", destinationIds);
        command.Parameters.AddWithValue("keys", keys);

        var accounts = new Dictionary<Guid, BankAccount>();
        var existing = new Dictionary<string, IdempotencyRow>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            accounts[reader.GetGuid(0)] = new BankAccount(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.GetBoolean(8));
        }

        if (!await reader.NextResultAsync(ct))
            throw new InvalidOperationException("Batch preparation did not return idempotency rows.");
        while (await reader.ReadAsync(ct))
        {
            existing[reader.GetString(8)] = new IdempotencyRow(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetDecimal(4), reader.GetGuid(5), reader.GetDecimal(6), reader.GetDecimal(7));
        }

        foreach (var commandItem in commands)
        {
            if (!accounts.ContainsKey(commandItem.SourceAccountId))
                throw new InvalidOperationException($"Account '{commandItem.SourceAccountId}' was not found.");
            if (!accounts.ContainsKey(commandItem.DestinationAccountId))
                throw new InvalidOperationException($"Account '{commandItem.DestinationAccountId}' was not found.");
        }

        return new(accounts, existing);
    }

    private static async Task ApplyBatchMutationCteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, int tenantId,
        Guid[] sources, Guid[] destinations, decimal[] amounts, string[] keys, Guid[] transferIds,
        decimal[] sourceBalances, decimal[] destinationBalances, CancellationToken ct)
    {
        const string sql = """
                           WITH input AS (
                               SELECT *
                               FROM unnest(
                                   @sources::uuid[], @destinations::uuid[], @amounts::numeric[], @keys::text[],
                                   @transfer_ids::uuid[], @source_balances::numeric[], @destination_balances::numeric[]
                               ) AS t(source_id, destination_id, amount, idempotency_key, transfer_id, source_balance, destination_balance)
                           ),
                           outgoing AS (
                               SELECT source_id AS account_id, SUM(amount) AS amount
                               FROM input GROUP BY source_id
                           ),
                           incoming AS (
                               SELECT destination_id AS account_id, SUM(amount) AS amount
                               FROM input GROUP BY destination_id
                           ),
                           account_update AS (
                               UPDATE banking.bank_account a
                               SET balance = a.balance - COALESCE(o.amount, 0) + COALESCE(i.amount, 0),
                                   daily_transferred = a.daily_transferred + COALESCE(o.amount, 0)
                               FROM outgoing o
                               FULL OUTER JOIN incoming i ON i.account_id = o.account_id
                               WHERE a.id = COALESCE(o.account_id, i.account_id)
                               RETURNING a.id
                           ),
                           idempotency_insert AS (
                               INSERT INTO banking.transfer_idempotency
                                   (idempotency_key, actor_id, tenant_id, source_account_id,
                                    destination_account_id, amount, transfer_id, source_balance, destination_balance)
                               SELECT idempotency_key, @actor, @tenant, source_id, destination_id,
                                      amount, transfer_id, source_balance, destination_balance
                               FROM input
                               RETURNING transfer_id, idempotency_key
                           ),
                           audit_insert AS (
                               INSERT INTO banking.transfer_audit
                                   (transfer_id, action, actor_id, tenant_id, source_account_id,
                                    destination_account_id, amount)
                               SELECT i.transfer_id, 'transferFunds', @actor, @tenant,
                                      i.source_id, i.destination_id, i.amount
                               FROM input i
                               JOIN idempotency_insert x ON x.transfer_id = i.transfer_id
                               RETURNING id
                           )
                           SELECT count(*) FROM account_update;
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("sources", sources);
        command.Parameters.AddWithValue("destinations", destinations);
        command.Parameters.AddWithValue("amounts", amounts);
        command.Parameters.AddWithValue("keys", keys);
        command.Parameters.AddWithValue("transfer_ids", transferIds);
        command.Parameters.AddWithValue("source_balances", sourceBalances);
        command.Parameters.AddWithValue("destination_balances", destinationBalances);
        var count = await command.ExecuteScalarAsync(ct);
        if (count is not long updated || updated <= 0)
            throw new InvalidOperationException("PostgreSQL batch mutation did not update any account rows.");
    }

    private async Task<AuthorizationContextRow?> LoadAuthorizationContextForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorId,
        int tenantId,
        CancellationToken cancellationToken)
    {
        if (_authorizationContextStore is null)
            return null;

        var context = await _authorizationContextStore.LoadForUpdateAsync(
            connection, transaction, actorId, tenantId, cancellationToken);
        if (context is null)
            throw new InvalidOperationException(
                "Authoritative authorization context does not exist; authorization is fail-closed.");
        return context;
    }

    private static void EnsureAuthorizationEvidenceMatchesStore(
        AuthorizationDecision decision,
        AuthorizationContextRow? context,
        bool authoritativeStoreConfigured)
    {
        if (context is null)
        {
            if (authoritativeStoreConfigured)
                throw new InvalidOperationException(
                    "Authoritative authorization context is missing; authorization fails closed.");
            return;
        }

        if (!context.Allowed)
            throw new SemanticAuthorizationException(
                "Authoritative authorization context is not allowed for this actor and tenant.");

        if (decision.Version != context.Version ||
            !string.Equals(decision.Fingerprint, context.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Authorization decision does not match the authoritative PostgreSQL authorization context.");
        }
    }

    private static void EnsureAuthorized(AuthorizationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.Allowed)
            throw new SemanticAuthorizationException(
                "Transfer capability is not authorized for this actor and account pair.");
    }

    private static void EnsureAuthorizationEvidenceMatches(
        AuthorizationDecision expected,
        AuthorizationDecision current)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(current);
        if (expected.Version < 0 || current.Version < 0 ||
            string.IsNullOrWhiteSpace(expected.Fingerprint) ||
            string.IsNullOrWhiteSpace(current.Fingerprint))
        {
            throw new InvalidOperationException("Authorization evidence is invalid.");
        }

        if (!current.Allowed)
            throw new SemanticAuthorizationException(
                "Transfer authorization was revoked before commit.");

        if (expected.Version != current.Version ||
            !string.Equals(expected.Fingerprint, current.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Authorization context changed during transfer execution; the authorization evidence is stale.");
        }
    }

    private static void ValidateShape(TransferFundsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Amount <= 0) throw new InvalidOperationException("Transfer amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new InvalidOperationException("Idempotency key is required.");
        if (command.SourceAccountId == command.DestinationAccountId)
            throw new InvalidOperationException("Source and destination accounts must differ.");
    }

    private static void ValidateExecution(Guid actorId, int tenantId, BankAccount source, BankAccount destination,
        decimal amount)
    {
        if (source.TenantId != tenantId || destination.TenantId != tenantId)
            throw new InvalidOperationException("Tenant boundary violation.");
        if (source.OwnerId != actorId || destination.OwnerId != actorId)
            throw new SemanticAuthorizationException("Transfer capability requires ownership of both accounts.");
        if (source.IsFrozen || destination.IsFrozen)
            throw new InvalidOperationException("Frozen accounts cannot participate in transfers.");
        if (source.DailyTransferred + amount > source.DailyLimit)
            throw new InvalidOperationException("Transfer exceeds the source account daily limit.");
        var available = source.Balance - source.PendingTransactions - source.RegulatoryHold;
        if (available < amount)
            throw new InvalidOperationException("Insufficient available funds.");
    }

    private static async Task AdvisoryLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<Guid, BankAccount>> LoadAccountsForUpdateAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid[] ids, CancellationToken ct)
    {
        const string sql = """
                           SELECT id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold,
                                  daily_transferred, daily_limit, is_frozen
                           FROM banking.bank_account
                           WHERE id = ANY(@ids::uuid[])
                           ORDER BY id
                           FOR UPDATE;
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ids", ids.Distinct().ToArray());
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new Dictionary<Guid, BankAccount>();
        while (await reader.ReadAsync(ct))
            result[reader.GetGuid(0)] = new(reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6),
                reader.GetDecimal(7), reader.GetBoolean(8));
        return result;
    }

    private static async Task<IdempotencyRow?> FindIdempotencyAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken ct)
    {
        const string sql = """
                           SELECT actor_id, tenant_id, source_account_id, destination_account_id, amount,
                                  transfer_id, source_balance, destination_balance
                           FROM banking.transfer_idempotency WHERE idempotency_key = @key;
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetDecimal(4),
            reader.GetGuid(5), reader.GetDecimal(6), reader.GetDecimal(7));
    }

    private static async Task ApplyMutationCteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        BankAccount source, BankAccount destination, TransferFundsCommand command, Guid actorId, int tenantId,
        Guid transferId, decimal sourceBalance, decimal destinationBalance, CancellationToken ct)
    {
        const string sql = """
                           WITH source_update AS (
                               UPDATE banking.bank_account SET balance = balance - @amount, daily_transferred = daily_transferred + @amount WHERE id = @source RETURNING id
                           ), destination_update AS (
                               UPDATE banking.bank_account SET balance = balance + @amount WHERE id = @destination RETURNING id
                           ), idempotency_insert AS (
                               INSERT INTO banking.transfer_idempotency
                                   (idempotency_key, actor_id, tenant_id, source_account_id, destination_account_id, amount, transfer_id, source_balance, destination_balance)
                               SELECT @key, @actor, @tenant, @source, @destination, @amount, @transfer, @source_balance, @destination_balance
                               FROM source_update CROSS JOIN destination_update RETURNING transfer_id
                           ), audit_insert AS (
                               INSERT INTO banking.transfer_audit
                                   (transfer_id, action, actor_id, tenant_id, source_account_id, destination_account_id, amount)
                               SELECT transfer_id, 'transferFunds', @actor, @tenant, @source, @destination, @amount FROM idempotency_insert RETURNING id
                           ) SELECT transfer_id FROM idempotency_insert;
                           """;
        await using var commandDb = new NpgsqlCommand(sql, connection, transaction);
        commandDb.Parameters.AddWithValue("key", command.IdempotencyKey);
        commandDb.Parameters.AddWithValue("actor", actorId);
        commandDb.Parameters.AddWithValue("tenant", tenantId);
        commandDb.Parameters.AddWithValue("source", source.Id);
        commandDb.Parameters.AddWithValue("destination", destination.Id);
        commandDb.Parameters.AddWithValue("amount", command.Amount);
        commandDb.Parameters.AddWithValue("transfer", transferId);
        commandDb.Parameters.AddWithValue("source_balance", sourceBalance);
        commandDb.Parameters.AddWithValue("destination_balance", destinationBalance);
        var returnedTransferId = await commandDb.ExecuteScalarAsync(ct);
        if (returnedTransferId is not Guid actualTransferId || actualTransferId != transferId)
            throw new InvalidOperationException("PostgreSQL mutation did not return the expected transfer identity.");
    }

    private static void EnsureReplayMatches(IdempotencyRow row, Guid actorId, int tenantId,
        TransferFundsCommand command)
    {
        if (row.ActorId != actorId || row.TenantId != tenantId || row.SourceAccountId != command.SourceAccountId ||
            row.DestinationAccountId != command.DestinationAccountId || row.Amount != command.Amount)
            throw new InvalidOperationException(
                "The idempotency key is already bound to a different transfer request.");
    }

    private sealed record IdempotencyRow(
        Guid ActorId,
        int TenantId,
        Guid SourceAccountId,
        Guid DestinationAccountId,
        decimal Amount,
        Guid TransferId,
        decimal SourceBalance,
        decimal DestinationBalance);
}

public sealed record PostgresTransferFundsExecutionResult(
    Guid TransferId,
    decimal SourceBalance,
    decimal DestinationBalance,
    bool Replay,
    long AuthorizationVersion = 0,
    string? AuthorizationFingerprint = null);