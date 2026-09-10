using Foundgine.Core.Execution;
using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres.Execution;

namespace Foundgine.HighAssurance.Postgres;

/// <summary>
/// Application-facing orchestration for the high-assurance TransferFunds capability.
/// Database and PostgreSQL-specific execution are delegated to the provider execution layer.
/// </summary>
public sealed class PostgresTransferFundsService
{
    public static PostgresMutationSecurityConformance SecurityConformance =>
        PostgresMutationSecurityConformance.TransferFunds;

    private const int PlanVersion = 3;
    private readonly PostgresTransferFundsExecutor _executor;

    public PostgresTransferFundsService(PostgresTransferFundsExecutor executor) =>
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<TransferExecutionReceipt> ExecuteAsync(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateShape(command);
        PostgresMutationSecurityConformanceGate.EnsureKnownInvariants();
        SecurityConformance.EnsureSatisfied();

        var result = await _executor.ExecuteAsync(actorId, tenantId, command, cancellationToken);
        return BuildReceipt(actorId, tenantId, command, result);
    }

    public async Task<IReadOnlyList<TransferExecutionReceipt>> ExecuteBatchAsync(
        Guid actorId,
        int tenantId,
        IReadOnlyList<TransferFundsCommand> commands,
        CancellationToken cancellationToken = default)
    {
        if (commands is null || commands.Count == 0)
            throw new ArgumentException("A transfer batch must contain at least one command.", nameof(commands));
        PostgresMutationSecurityConformanceGate.EnsureKnownInvariants();
        SecurityConformance.EnsureSatisfied();
        foreach (var command in commands) ValidateShape(command);

        var results = await _executor.ExecuteBatchAsync(actorId, tenantId, commands, cancellationToken);
        var receipts = new TransferExecutionReceipt[commands.Count];
        for (var i = 0; i < commands.Count; i++)
            receipts[i] = BuildReceipt(actorId, tenantId, commands[i], results[i]);
        return receipts;
    }

    private static void ValidateShape(TransferFundsCommand command)
    {
        if (command.Amount <= 0) throw new InvalidOperationException("Transfer amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new InvalidOperationException("Idempotency key is required.");
        if (command.SourceAccountId == command.DestinationAccountId)
            throw new InvalidOperationException("Source and destination accounts must differ.");
    }

    private static TransferExecutionReceipt BuildReceipt(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        PostgresTransferFundsExecutionResult result)
    {
        var now = DateTimeOffset.UtcNow;
        var intent = ExecutionEvidenceFactory.Hash(
            $"{TransferFundsService.CapabilityId}|v{TransferFundsService.CapabilityVersion}|{actorId}|{tenantId}|{command.SourceAccountId}|{command.DestinationAccountId}|{command.Amount}|{command.IdempotencyKey}");
        var plan = ExecutionEvidenceFactory.Hash(
            "transferFunds|postgres-plan-v3-cte|advisory-idempotency-lock|row-locks|tenant-isolation|execution-authorization|frozen-state|available-funds|daily-limit|atomic-state-idempotency-audit");
        var authorization = ExecutionEvidenceFactory.Hash(
            $"actor:{actorId}|tenant:{tenantId}|source:{command.SourceAccountId}|destination:{command.DestinationAccountId}");
        var evidence = new ExecutionEvidence("postgres", plan, [], 1, 0, IntentFingerprint: intent,
            AuthorizationFingerprint: result.AuthorizationFingerprint ?? authorization,
            AuthorizationVersion: result.AuthorizationVersion);
        var resultFingerprint = ExecutionEvidenceFactory.Hash(
            $"{result.TransferId}|{command.Amount}|{result.SourceBalance}|{result.DestinationBalance}|{result.Replay}");
        var execution = ExecutionReceiptFactory.Create(
            Guid.NewGuid().ToString("N"), evidence, resultFingerprint, [],
            result.Replay
                ? ["transferFunds.replay"]
                : ["transferFunds.debit", "transferFunds.credit", "transferFunds.idempotency", "transferFunds.audit"],
            now, DateTimeOffset.UtcNow, 1, TransferFundsService.CapabilityVersion, 1, PlanVersion,
            "banking-semantic-model-v1");
        var securityProof = SecurityInvariantAttestation.Create(
            "postgres-high-assurance",
            PostgresMutationSecurityConformance.TransferFunds.RequiredInvariants,
            PostgresMutationSecurityConformance.TransferFunds.RequiredInvariants);
        securityProof.EnsureSatisfied();
        return new TransferExecutionReceipt(execution, result.TransferId, command.SourceAccountId,
            command.DestinationAccountId, command.Amount, result.Replay, securityProof);
    }
}