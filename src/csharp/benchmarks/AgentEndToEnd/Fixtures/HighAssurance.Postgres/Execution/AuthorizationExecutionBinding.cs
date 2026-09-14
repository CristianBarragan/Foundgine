using Foundgine.Core.Execution;
using Foundgine.HighAssurance.Banking;

namespace Foundgine.HighAssurance.Postgres.Execution;

/// <summary>
/// execution-time binding between authorization evidence and the exact
/// mutation being executed. The binding is deliberately not part of plan identity.
/// </summary>
public sealed record AuthorizationExecutionBinding(
    Guid ActorId,
    int TenantId,
    string Operation,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string IdempotencyKey,
    long AuthorizationVersion,
    string AuthorizationFingerprint,
    string BindingFingerprint)
{
    public static AuthorizationExecutionBinding Create(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        AuthorizationDecision authorization)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!authorization.Allowed)
            throw new UnauthorizedAccessException("Denied authorization evidence cannot be bound to an execution.");
        if (string.IsNullOrWhiteSpace(authorization.Fingerprint))
            throw new InvalidOperationException("Authorization evidence fingerprint is required.");

        var canonical = string.Join("|", new[]
        {
            "foundgine.authorization-execution-binding.v1",
            "transferFunds",
            actorId.ToString("D"),
            tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            command.SourceAccountId.ToString("D"),
            command.DestinationAccountId.ToString("D"),
            command.Amount.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
            command.IdempotencyKey,
            authorization.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            authorization.Fingerprint
        });

        return new(
            actorId, tenantId, "transferFunds", command.SourceAccountId,
            command.DestinationAccountId, command.Amount, command.IdempotencyKey,
            authorization.Version, authorization.Fingerprint,
            ExecutionEvidenceFactory.Hash(canonical));
    }

    public void ValidateAgainst(
        Guid actorId,
        int tenantId,
        TransferFundsCommand command,
        AuthorizationDecision authorization)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(authorization);

        var current = Create(actorId, tenantId, command, authorization);
        if (ActorId != current.ActorId || TenantId != current.TenantId ||
            Operation != current.Operation || SourceAccountId != current.SourceAccountId ||
            DestinationAccountId != current.DestinationAccountId || Amount != current.Amount ||
            !string.Equals(IdempotencyKey, current.IdempotencyKey, StringComparison.Ordinal) ||
            AuthorizationVersion != current.AuthorizationVersion ||
            !string.Equals(AuthorizationFingerprint, current.AuthorizationFingerprint, StringComparison.Ordinal) ||
            !string.Equals(BindingFingerprint, current.BindingFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Authorization evidence is not bound to the exact execution request; authorization fails closed.");
        }
    }
}