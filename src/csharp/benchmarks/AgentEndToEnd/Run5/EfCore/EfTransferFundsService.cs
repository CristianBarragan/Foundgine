using Foundgine.Core.Execution;
using Foundgine.HighAssurance.Banking;
using Foundgine.Core.Semantic.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.HighAssurance.EfCore;

/// <summary>
/// EF Core execution boundary for the high-assurance TransferFunds capability.
/// Mirrors Foundgine.HighAssurance.Postgres.PostgresTransferFundsService: same
/// business semantics (Foundgine.HighAssurance.Banking.TransferFundsService),
/// same `banking` schema, same locking discipline (advisory lock + deterministic
/// FOR UPDATE row order) — but state transitions are applied through EF Core's
/// change tracker and a single SaveChangesAsync instead of hand-written SQL.
/// </summary>
public sealed class EfTransferFundsService
{
    private const int PlanVersion = 3;

    private readonly BankingDbContext _db;
    private readonly Func<Guid, BankAccount, BankAccount, bool> _authorize;

    public EfTransferFundsService(BankingDbContext db, Func<Guid, BankAccount, BankAccount, bool> authorize)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
    }

    public async Task<TransferExecutionReceipt> ExecuteAsync(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateShape(command);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Same advisory-lock discipline as the raw-ADO.NET provider: serialize
            // all requests sharing an idempotency key before touching account rows.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({command.IdempotencyKey}, 0));",
                cancellationToken);

            var existing = await _db.TransferIdempotency
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == command.IdempotencyKey, cancellationToken);

            if (existing is not null)
            {
                EnsureReplayMatches(existing, actorId, tenantId, command);
                await transaction.CommitAsync(cancellationToken);
                return BuildReceipt(actorId, tenantId, command, existing.TransferId, existing.SourceBalance,
                    existing.DestinationBalance, replay: true);
            }

            var sourceId = command.SourceAccountId;
            var destinationId = command.DestinationAccountId;
            var firstId = sourceId.CompareTo(destinationId) < 0 ? sourceId : destinationId;
            var secondId = firstId == sourceId ? destinationId : sourceId;

            // Deterministic row-lock order avoids A->B / B->A deadlocks, same as the
            // raw-ADO.NET provider. EF Core has no first-class FOR UPDATE, so the
            // locking read is raw SQL, but the resulting entities are tracked and
            // subsequent mutation goes through the change tracker.
            var first = await _db.BankAccounts
                            .FromSqlInterpolated($"SELECT * FROM banking.bank_account WHERE id = {firstId} FOR UPDATE")
                            .SingleOrDefaultAsync(cancellationToken)
                        ?? throw new InvalidOperationException($"Account '{firstId}' was not found.");
            var second = await _db.BankAccounts
                             .FromSqlInterpolated(
                                 $"SELECT * FROM banking.bank_account WHERE id = {secondId} FOR UPDATE")
                             .SingleOrDefaultAsync(cancellationToken)
                         ?? throw new InvalidOperationException($"Account '{secondId}' was not found.");

            var sourceRow = first.Id == sourceId ? first : second;
            var destinationRow = first.Id == destinationId ? first : second;

            var source = ToDomain(sourceRow);
            var destination = ToDomain(destinationRow);
            ValidateExecution(actorId, tenantId, source, destination, command.Amount);

            // Authorization is deliberately re-checked after the locking read and
            // immediately before the state transition — not trusted from planning time.
            if (!_authorize(actorId, source, destination))
                throw new SemanticAuthorizationException(
                    "Transfer capability is not authorized for this actor and account pair.");

            var transferId = Guid.NewGuid();
            var sourceBalance = sourceRow.Balance - command.Amount;
            var destinationBalance = destinationRow.Balance + command.Amount;

            sourceRow.Balance = sourceBalance;
            sourceRow.DailyTransferred += command.Amount;
            destinationRow.Balance = destinationBalance;

            _db.TransferIdempotency.Add(new TransferIdempotencyRow
            {
                IdempotencyKey = command.IdempotencyKey,
                ActorId = actorId,
                TenantId = tenantId,
                SourceAccountId = command.SourceAccountId,
                DestinationAccountId = command.DestinationAccountId,
                Amount = command.Amount,
                TransferId = transferId,
                SourceBalance = sourceBalance,
                DestinationBalance = destinationBalance,
                CreatedAt = DateTimeOffset.UtcNow
            });

            _db.TransferAudit.Add(new TransferAuditRow
            {
                TransferId = transferId,
                Action = "transferFunds",
                ActorId = actorId,
                TenantId = tenantId,
                SourceAccountId = command.SourceAccountId,
                DestinationAccountId = command.DestinationAccountId,
                Amount = command.Amount,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // One round-trip: source/destination balance updates plus both inserts
            // are batched into a single SaveChangesAsync call.
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return BuildReceipt(actorId, tenantId, command, transferId, sourceBalance, destinationBalance,
                replay: false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    private static BankAccount ToDomain(BankAccountRow row) => new(
        row.Id, row.TenantId, row.OwnerId, row.Balance, row.PendingTransactions,
        row.RegulatoryHold, row.DailyTransferred, row.DailyLimit, row.IsFrozen);

    private static void ValidateShape(TransferFundsCommand command)
    {
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
        if (source.IsFrozen || destination.IsFrozen)
            throw new InvalidOperationException("Frozen accounts cannot participate in transfers.");
        if (source.DailyTransferred + amount > source.DailyLimit)
            throw new InvalidOperationException("Transfer exceeds the source account daily limit.");
        var available = source.Balance - source.PendingTransactions - source.RegulatoryHold;
        if (available < amount)
            throw new InvalidOperationException("Insufficient available funds.");
    }

    private static void EnsureReplayMatches(TransferIdempotencyRow row, Guid actorId, int tenantId,
        TransferFundsCommand command)
    {
        if (row.ActorId != actorId || row.TenantId != tenantId || row.SourceAccountId != command.SourceAccountId ||
            row.DestinationAccountId != command.DestinationAccountId || row.Amount != command.Amount)
            throw new InvalidOperationException(
                "The idempotency key is already bound to a different transfer request.");
    }

    private static TransferExecutionReceipt BuildReceipt(
        Guid actorId, int tenantId, TransferFundsCommand command, Guid transferId,
        decimal sourceBalance, decimal destinationBalance, bool replay)
    {
        var now = DateTimeOffset.UtcNow;
        var intent = ExecutionEvidenceFactory.Hash(
            $"{TransferFundsService.CapabilityId}|v{TransferFundsService.CapabilityVersion}|{actorId}|{tenantId}|{command.SourceAccountId}|{command.DestinationAccountId}|{command.Amount}|{command.IdempotencyKey}");
        var plan = ExecutionEvidenceFactory.Hash(
            "transferFunds|efcore-plan-v3|advisory-idempotency-lock|row-locks|tenant-isolation|execution-authorization|frozen-state|available-funds|daily-limit|atomic-state-idempotency-audit");
        var authorization = ExecutionEvidenceFactory.Hash(
            $"actor:{actorId}|tenant:{tenantId}|source:{command.SourceAccountId}|destination:{command.DestinationAccountId}");
        var evidence = new ExecutionEvidence("ef-core", plan, [], 1, 0, IntentFingerprint: intent,
            AuthorizationFingerprint: authorization);
        var resultFingerprint =
            ExecutionEvidenceFactory.Hash(
                $"{transferId}|{command.Amount}|{sourceBalance}|{destinationBalance}|{replay}");
        var execution = ExecutionReceiptFactory.Create(
            Guid.NewGuid().ToString("N"), evidence, resultFingerprint, [],
            replay
                ? ["transferFunds.replay"]
                : ["transferFunds.debit", "transferFunds.credit", "transferFunds.idempotency", "transferFunds.audit"],
            now, DateTimeOffset.UtcNow, 1, TransferFundsService.CapabilityVersion, 1, PlanVersion,
            "banking-semantic-model-v1");
        return new TransferExecutionReceipt(execution, transferId, command.SourceAccountId,
            command.DestinationAccountId, command.Amount, replay);
    }
}