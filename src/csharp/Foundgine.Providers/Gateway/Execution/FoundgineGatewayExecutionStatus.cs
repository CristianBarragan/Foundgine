namespace Foundgine.Providers.Gateway.Execution;

public sealed record FoundgineGatewayExecutionStatus(
    string ExecutionId,
    string Status,
    string OperationKind,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? OutcomeCode);
