using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;

namespace Foundgine.HighAssurance.Banking;

public sealed record BankAccount(
    Guid Id,
    int TenantId,
    Guid OwnerId,
    decimal Balance,
    decimal PendingTransactions,
    decimal RegulatoryHold,
    decimal DailyTransferred,
    decimal DailyLimit,
    bool IsFrozen);

public sealed record TransferFundsCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string IdempotencyKey);

public sealed record TransferExecutionReceipt(
    ExecutionReceipt Execution,
    Guid TransferId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    bool Replay,
    SecurityInvariantAttestation? SecurityProof = null);

public sealed record AuditEntry(
    Guid TransferId,
    string Action,
    Guid ActorId,
    int TenantId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    DateTimeOffset At);

public interface IBankAuthorization
{
    bool CanTransfer(Guid actorId, BankAccount source, BankAccount destination);
}

public interface IBankAuditSink
{
    void Append(AuditEntry entry);
}

public sealed class InMemoryBankAuditSink : IBankAuditSink
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();
    public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();
    public void Append(AuditEntry entry) => _entries.Enqueue(entry);
}

public sealed class OwnershipAuthorization : IBankAuthorization
{
    public bool CanTransfer(Guid actorId, BankAccount source, BankAccount destination) =>
        source.OwnerId == actorId && destination.OwnerId == actorId && source.TenantId == destination.TenantId;
}

public sealed class BankAccountStore
{
    private readonly ConcurrentDictionary<Guid, BankAccount> _accounts = new();
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _idempotency = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, object> _locks = new();

    public void Add(BankAccount account) => _accounts[account.Id] = account;

    public BankAccount Get(Guid id) => _accounts.TryGetValue(id, out var account)
        ? account
        : throw new InvalidOperationException($"Account '{id}' was not found.");

    internal object LockFor(Guid id) => _locks.GetOrAdd(id, static _ => new object());

    internal bool TryGetIdempotent(string key, out IdempotencyRecord record) =>
        _idempotency.TryGetValue(key, out record!);

    internal void StoreIdempotent(string key, IdempotencyRecord record) => _idempotency[key] = record;
    internal void Replace(BankAccount account) => _accounts[account.Id] = account;
}

public sealed record TransferFundsResult(
    Guid TransferId,
    decimal Amount,
    decimal SourceBalance,
    decimal DestinationBalance);

internal sealed record IdempotencyRecord(
    Guid ActorId,
    int TenantId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    TransferFundsResult Result);

/// <summary>
/// Flagship consequential mutation. The business meaning is explicit here rather
/// than inferred from CRUD fields: available funds = balance - pending - regulatory hold.
/// The execution boundary rechecks authorization and all invariants while holding
/// deterministic locks for both accounts, then records an audit event and idempotency result.
/// </summary>
public sealed class TransferFundsService
{
    public const string CapabilityId = "BankAccount.transferFunds";
    public const int CapabilityVersion = 1;
    private const int PlanVersion = 1;

    private readonly BankAccountStore _store;
    private readonly IBankAuthorization _authorization;
    private readonly IBankAuditSink _audit;

    public TransferFundsService(BankAccountStore store, IBankAuthorization authorization, IBankAuditSink audit)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public TransferExecutionReceipt Execute(Guid actorId, int tenantId, TransferFundsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Amount <= 0) throw new InvalidOperationException("Transfer amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new InvalidOperationException("Idempotency key is required.");
        if (command.SourceAccountId == command.DestinationAccountId)
            throw new InvalidOperationException("Source and destination accounts must differ.");

        var sourceBeforeLock = _store.Get(command.SourceAccountId);
        var destinationBeforeLock = _store.Get(command.DestinationAccountId);
        Validate(actorId, tenantId, sourceBeforeLock, destinationBeforeLock, command.Amount);

        if (_store.TryGetIdempotent(command.IdempotencyKey, out var replayRecord))
        {
            EnsureReplayMatches(replayRecord, actorId, tenantId, command);
            return BuildReceipt(actorId, tenantId, command, replayRecord.Result, replay: true);
        }

        var first = command.SourceAccountId.CompareTo(command.DestinationAccountId) < 0
            ? command.SourceAccountId
            : command.DestinationAccountId;
        var second = first == command.SourceAccountId ? command.DestinationAccountId : command.SourceAccountId;
        lock (_store.LockFor(first))
        lock (_store.LockFor(second))
        {
            // Re-read after locking. Authorization is intentionally execution-time, not cached from planning.
            var source = _store.Get(command.SourceAccountId);
            var destination = _store.Get(command.DestinationAccountId);
            Validate(actorId, tenantId, source, destination, command.Amount);

            if (_store.TryGetIdempotent(command.IdempotencyKey, out replayRecord))
            {
                EnsureReplayMatches(replayRecord, actorId, tenantId, command);
                return BuildReceipt(actorId, tenantId, command, replayRecord.Result, replay: true);
            }

            var transferId = Guid.NewGuid();
            var sourceAvailable = source.Balance - source.PendingTransactions - source.RegulatoryHold;
            var destinationAvailable =
                destination.Balance - destination.PendingTransactions - destination.RegulatoryHold;
            var result = new TransferFundsResult(
                transferId,
                command.Amount,
                source.Balance - command.Amount,
                destination.Balance + command.Amount);

            _store.Replace(source with
            {
                Balance = source.Balance - command.Amount,
                DailyTransferred = source.DailyTransferred + command.Amount
            });
            _store.Replace(destination with { Balance = destination.Balance + command.Amount });
            _store.StoreIdempotent(command.IdempotencyKey,
                new IdempotencyRecord(actorId, tenantId, command.SourceAccountId, command.DestinationAccountId,
                    command.Amount, result));

            _audit.Append(new AuditEntry(
                transferId, "transferFunds", actorId, tenantId,
                source.Id, destination.Id, command.Amount, DateTimeOffset.UtcNow));

            return BuildReceipt(actorId, tenantId, command, result, replay: false,
                sourceAvailable, destinationAvailable);
        }
    }

    private static void EnsureReplayMatches(IdempotencyRecord record, Guid actorId, int tenantId,
        TransferFundsCommand command)
    {
        if (record.ActorId != actorId || record.TenantId != tenantId ||
            record.SourceAccountId != command.SourceAccountId ||
            record.DestinationAccountId != command.DestinationAccountId ||
            record.Amount != command.Amount)
            throw new InvalidOperationException(
                "The idempotency key is already bound to a different transfer request.");
    }

    private void Validate(Guid actorId, int tenantId, BankAccount source, BankAccount destination, decimal amount)
    {
        if (source.TenantId != tenantId || destination.TenantId != tenantId)
            throw new InvalidOperationException("Tenant boundary violation.");
        if (!_authorization.CanTransfer(actorId, source, destination))
            throw new SemanticAuthorizationException(
                "Transfer capability is not authorized for this actor and account pair.");
        if (source.IsFrozen || destination.IsFrozen)
            throw new InvalidOperationException("Frozen accounts cannot participate in transfers.");
        if (source.DailyTransferred + amount > source.DailyLimit)
            throw new InvalidOperationException("Transfer exceeds the source account daily limit.");
        var available = source.Balance - source.PendingTransactions - source.RegulatoryHold;
        if (available < amount)
            throw new InvalidOperationException("Insufficient available funds.");
    }

    private static TransferExecutionReceipt BuildReceipt(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        TransferFundsResult result,
        bool replay,
        decimal? sourceAvailable = null,
        decimal? destinationAvailable = null)
    {
        var started = DateTimeOffset.UtcNow;
        var intent = ExecutionEvidenceFactory.Hash(
            $"{CapabilityId}|v{CapabilityVersion}|{actorId}|{tenantId}|{command.SourceAccountId}|{command.DestinationAccountId}|{command.Amount}|{command.IdempotencyKey}");
        var plan = ExecutionEvidenceFactory.Hash(
            $"transferFunds|plan-v{PlanVersion}|source-authorization|destination-authorization|tenant-isolation|frozen-state|available-funds|daily-limit|idempotency|atomic-debit-credit|audit");
        var authorization = ExecutionEvidenceFactory.Hash(
            $"actor:{actorId}|tenant:{tenantId}|source:{command.SourceAccountId}|destination:{command.DestinationAccountId}");
        var provider = "high-assurance-banking";
        var evidence = new ExecutionEvidence(provider, plan, [], 1, 0, IntentFingerprint: intent,
            AuthorizationFingerprint: authorization);
        var resultFingerprint = ExecutionEvidenceFactory.Hash(
            $"{result.TransferId}|{result.Amount}|{result.SourceBalance}|{result.DestinationBalance}|{replay}");
        var execution = ExecutionReceiptFactory.Create(
            Guid.NewGuid().ToString("N"), evidence, resultFingerprint,
            [],
            replay ? ["transferFunds.replay"] : ["transferFunds.debit", "transferFunds.credit", "transferFunds.audit"],
            started, DateTimeOffset.UtcNow, 1, CapabilityVersion, 1, PlanVersion, "banking-semantic-model-v1");
        return new TransferExecutionReceipt(execution, result.TransferId, command.SourceAccountId,
            command.DestinationAccountId, command.Amount, replay);
    }

    public static SemanticCapability DescribeCapability() => new(
        CapabilityId,
        "Transfer funds between two owned accounts",
        new EntityId(1000),
        new AuthorizationDecision(AuthorizationAccess.Allowed),
        [
            new("sourceAccountId", "uuid", true, "Source account."),
            new("destinationAccountId", "uuid", true, "Destination account."),
            new("amount", "decimal", true, "Positive transfer amount."),
            new("idempotencyKey", "string", true, "Unique replay-protection key.")
        ],
        [
            new("same-tenant", "Source and destination accounts must belong to the request tenant."),
            new("ownership", "The actor must own both accounts."),
            new("not-frozen", "Neither account may be frozen."),
            new("daily-limit",
                "The source account daily transferred amount plus this transfer must not exceed its daily limit."),
            new("available-funds",
                "Balance minus pending transactions minus regulatory holds must cover the transfer."),
            new("idempotency",
                "A previously committed idempotency key must return the original result without applying a second transfer."),
            new("atomicity", "Debit, credit, idempotency record and audit event form one logical atomic operation.")
        ],
        [
            new("account.debit", "Decrease source account balance."),
            new("account.credit", "Increase destination account balance."),
            new("transfer.audit", "Record the consequential mutation.")
        ],
        ["sourceAccountId", "destinationAccountId", "amount", "idempotencyKey"], [])
    {
        Operation = "transferFunds",
        HasSideEffects = true,
        IsIdempotent = true,
        Version = CapabilityVersion
    };
}