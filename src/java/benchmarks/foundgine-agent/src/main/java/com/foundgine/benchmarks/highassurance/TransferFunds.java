package com.foundgine.benchmarks.highassurance;

import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;

import java.math.BigDecimal;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;

public final class TransferFunds {
    public static final String CAPABILITY_ID = "BankAccount.transferFunds";

    public record BankAccount(UUID id, int tenantId, UUID ownerId, BigDecimal balance,
                              BigDecimal pendingTransactions, BigDecimal regulatoryHold,
                              BigDecimal dailyTransferred, BigDecimal dailyLimit, boolean frozen) {}
    public record Command(UUID sourceAccountId, UUID destinationAccountId, BigDecimal amount, String idempotencyKey) {}
    public record Result(UUID transferId, BigDecimal amount, BigDecimal sourceBalance, BigDecimal destinationBalance) {}
    public record Receipt(UUID transferId, UUID sourceAccountId, UUID destinationAccountId,
                          BigDecimal amount, boolean replay, String status, List<String> effects,
                          Instant at) {}
    public record AuditEntry(UUID transferId, String action, UUID actorId, int tenantId,
                             UUID sourceAccountId, UUID destinationAccountId, BigDecimal amount, Instant at) {}
    private record Idempotency(UUID actorId, int tenantId, UUID source, UUID destination, BigDecimal amount, Result result) {}

    public interface Authorization { boolean canTransfer(UUID actorId, BankAccount source, BankAccount destination); }
    public interface AuditSink { void append(AuditEntry entry); }

    public static final class OwnershipAuthorization implements Authorization {
        public boolean canTransfer(UUID actorId, BankAccount source, BankAccount destination) {
            return source.ownerId().equals(actorId) && destination.ownerId().equals(actorId)
                    && source.tenantId() == destination.tenantId();
        }
    }

    public static final class InMemoryAuditSink implements AuditSink {
        private final Queue<AuditEntry> entries = new ConcurrentLinkedQueue<>();
        public void append(AuditEntry entry) { entries.add(entry); }
        public List<AuditEntry> entries() { return List.copyOf(entries); }
    }

    public static final class Store {
        private final Map<UUID, BankAccount> accounts = new ConcurrentHashMap<>();
        private final Map<String, Idempotency> idempotency = new ConcurrentHashMap<>();
        private final Map<UUID, Object> locks = new ConcurrentHashMap<>();
        public void add(BankAccount account) { accounts.put(account.id(), account); }
        public BankAccount get(UUID id) { var a = accounts.get(id); if (a == null) throw new IllegalStateException("Account '" + id + "' was not found."); return a; }
        public void replace(BankAccount account) { accounts.put(account.id(), account); }
        Object lockFor(UUID id) { return locks.computeIfAbsent(id, x -> new Object()); }
        Idempotency idempotency(String key) { return idempotency.get(key); }
        void storeIdempotency(String key, Idempotency record) { idempotency.put(key, record); }
    }

    private final Store store;
    private final Authorization authorization;
    private final AuditSink audit;
    public TransferFunds(Store store, Authorization authorization, AuditSink audit) {
        this.store = Objects.requireNonNull(store); this.authorization = Objects.requireNonNull(authorization); this.audit = Objects.requireNonNull(audit);
    }

    public Receipt execute(UUID actorId, int tenantId, Command command) {
        Objects.requireNonNull(command);
        if (command.amount().signum() <= 0) throw new IllegalStateException("Transfer amount must be greater than zero.");
        if (command.idempotencyKey() == null || command.idempotencyKey().isBlank()) throw new IllegalStateException("Idempotency key is required.");
        if (command.sourceAccountId().equals(command.destinationAccountId())) throw new IllegalStateException("Source and destination accounts must differ.");
        var first = command.sourceAccountId().compareTo(command.destinationAccountId()) < 0 ? command.sourceAccountId() : command.destinationAccountId();
        var second = first.equals(command.sourceAccountId()) ? command.destinationAccountId() : command.sourceAccountId();
        synchronized (store.lockFor(first)) { synchronized (store.lockFor(second)) {
            var source = store.get(command.sourceAccountId()); var destination = store.get(command.destinationAccountId());
            validate(actorId, tenantId, source, destination, command.amount());
            var replay = store.idempotency(command.idempotencyKey());
            if (replay != null) { ensureReplayMatches(replay, actorId, tenantId, command); return receipt(replay.result(), command, true); }
            var transferId = UUID.randomUUID();
            var result = new Result(transferId, command.amount(), source.balance().subtract(command.amount()), destination.balance().add(command.amount()));
            store.replace(new BankAccount(source.id(), source.tenantId(), source.ownerId(), result.sourceBalance(), source.pendingTransactions(), source.regulatoryHold(), source.dailyTransferred().add(command.amount()), source.dailyLimit(), source.frozen()));
            store.replace(new BankAccount(destination.id(), destination.tenantId(), destination.ownerId(), result.destinationBalance(), destination.pendingTransactions(), destination.regulatoryHold(), destination.dailyTransferred(), destination.dailyLimit(), destination.frozen()));
            store.storeIdempotency(command.idempotencyKey(), new Idempotency(actorId, tenantId, command.sourceAccountId(), command.destinationAccountId(), command.amount(), result));
            audit.append(new AuditEntry(transferId, "transferFunds", actorId, tenantId, source.id(), destination.id(), command.amount(), Instant.now()));
            return receipt(result, command, false);
        }}
    }

    private void validate(UUID actorId, int tenantId, BankAccount source, BankAccount destination, BigDecimal amount) {
        if (source.tenantId() != tenantId || destination.tenantId() != tenantId) throw new IllegalStateException("Tenant boundary violation.");
        if (!authorization.canTransfer(actorId, source, destination)) throw new SemanticAuthorizationException("Transfer capability is not authorized for this actor and account pair.");
        if (source.frozen() || destination.frozen()) throw new IllegalStateException("Frozen accounts cannot participate in transfers.");
        if (source.dailyTransferred().add(amount).compareTo(source.dailyLimit()) > 0) throw new IllegalStateException("Transfer exceeds the source account daily limit.");
        var available = source.balance().subtract(source.pendingTransactions()).subtract(source.regulatoryHold());
        if (available.compareTo(amount) < 0) throw new IllegalStateException("Insufficient available funds.");
    }
    private static void ensureReplayMatches(Idempotency r, UUID actor, int tenant, Command c) {
        if (!r.actorId().equals(actor) || r.tenantId()!=tenant || !r.source().equals(c.sourceAccountId()) || !r.destination().equals(c.destinationAccountId()) || r.amount().compareTo(c.amount()) != 0)
            throw new IllegalStateException("The idempotency key is already bound to a different transfer request.");
    }
    private static Receipt receipt(Result r, Command c, boolean replay) {
        return new Receipt(r.transferId(), c.sourceAccountId(), c.destinationAccountId(), c.amount(), replay, "succeeded",
                replay ? List.of("transferFunds.replay") : List.of("transferFunds.debit", "transferFunds.credit", "transferFunds.audit"), Instant.now());
    }
}
