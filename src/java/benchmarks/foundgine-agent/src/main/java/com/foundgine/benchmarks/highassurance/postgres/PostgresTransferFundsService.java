package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.benchmarks.highassurance.TransferFunds;
import java.sql.SQLException;
import java.time.Instant;
import java.util.*;

/** Application-facing boundary for the PostgreSQL high-assurance TransferFunds executor. */
public final class PostgresTransferFundsService {
    public record Receipt(UUID transferId, UUID sourceAccountId, UUID destinationAccountId,
                          java.math.BigDecimal amount, boolean replay, Instant committedAt,
                          long authorizationVersion, String authorizationFingerprint) {}
    private final PostgresTransferFundsExecutor executor;
    public PostgresTransferFundsService(PostgresTransferFundsExecutor executor) { this.executor=Objects.requireNonNull(executor); }
    public Receipt execute(UUID actorId, int tenantId, TransferFunds.Command command) throws SQLException {
        var r=executor.execute(actorId, tenantId, command);
        return new Receipt(r.transferId(), command.sourceAccountId(), command.destinationAccountId(), command.amount(), r.replay(), Instant.now(), r.authorizationVersion(), r.authorizationFingerprint());
    }
}
