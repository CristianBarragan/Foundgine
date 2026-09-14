package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.benchmarks.highassurance.TransferFunds;
import com.foundgine.core.execution.ExecutionEvidenceFactory;

import java.math.BigDecimal;
import java.util.Objects;
import java.util.UUID;

/**
 * Execution-time binding between authorization evidence and the exact mutation
 * being executed. The binding is deliberately not part of semantic plan identity.
 * Port of the C# HighAssurance.Postgres AuthorizationExecutionBinding.
 */
public record AuthorizationExecutionBinding(
        UUID actorId,
        int tenantId,
        String operation,
        UUID sourceAccountId,
        UUID destinationAccountId,
        BigDecimal amount,
        String idempotencyKey,
        long authorizationVersion,
        String authorizationFingerprint,
        String bindingFingerprint) {

    public static AuthorizationExecutionBinding create(
            UUID actorId,
            int tenantId,
            TransferFunds.Command command,
            PostgresAuthorizationDecision authorization) {
        Objects.requireNonNull(actorId, "actorId");
        Objects.requireNonNull(command, "command");
        Objects.requireNonNull(authorization, "authorization");
        if (!authorization.allowed()) {
            throw new SecurityException("Denied authorization evidence cannot be bound to an execution.");
        }
        if (authorization.fingerprint() == null || authorization.fingerprint().isBlank()) {
            throw new IllegalStateException("Authorization evidence fingerprint is required.");
        }

        var canonical = String.join("|",
                "foundgine.authorization-execution-binding.v1",
                "transferFunds",
                actorId.toString(),
                Integer.toString(tenantId),
                command.sourceAccountId().toString(),
                command.destinationAccountId().toString(),
                canonicalAmount(command.amount()),
                command.idempotencyKey(),
                Long.toString(authorization.version()),
                authorization.fingerprint());

        return new AuthorizationExecutionBinding(
                actorId, tenantId, "transferFunds",
                command.sourceAccountId(), command.destinationAccountId(), command.amount(),
                command.idempotencyKey(), authorization.version(), authorization.fingerprint(),
                ExecutionEvidenceFactory.hash(canonical));
    }

    public void validateAgainst(
            UUID actorId,
            int tenantId,
            TransferFunds.Command command,
            PostgresAuthorizationDecision authorization) {
        var current = create(actorId, tenantId, command, authorization);
        if (!actorId().equals(current.actorId())
                || tenantId() != current.tenantId()
                || !operation().equals(current.operation())
                || !sourceAccountId().equals(current.sourceAccountId())
                || !destinationAccountId().equals(current.destinationAccountId())
                || amount().compareTo(current.amount()) != 0
                || !idempotencyKey().equals(current.idempotencyKey())
                || authorizationVersion() != current.authorizationVersion()
                || !authorizationFingerprint().equals(current.authorizationFingerprint())
                || !bindingFingerprint().equals(current.bindingFingerprint())) {
            throw new IllegalStateException(
                    "Authorization evidence is not bound to the exact execution request; authorization fails closed.");
        }
    }

    private static String canonicalAmount(BigDecimal amount) {
        Objects.requireNonNull(amount, "amount");
        // BigDecimal.toString preserves the C# G29 intent for ordinary monetary values
        // while remaining deterministic and avoiding locale-sensitive formatting.
        return amount.stripTrailingZeros().toPlainString();
    }
}
