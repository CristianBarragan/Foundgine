using Foundgine.HighAssurance.Banking;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.HighAssurance.Tests;

public sealed class TransferFundsTests
{
    private readonly Guid _actor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly int _tenant = 42;

    [Fact]
    public void Transfer_enforces_business_semantics_and_emits_receipt_and_audit()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 250m, dailyLimit: 5000m);
        var store = NewStore(source, destination);
        var audit = new InMemoryBankAuditSink();
        var service = new TransferFundsService(store, new OwnershipAuthorization(), audit);

        var receipt = service.Execute(_actor, _tenant,
            new TransferFundsCommand(source.Id, destination.Id, 400m, "k-1"));

        Assert.False(receipt.Replay);
        Assert.Equal(600m, store.Get(source.Id).Balance);
        Assert.Equal(650m, store.Get(destination.Id).Balance);
        Assert.Single(audit.Entries);
        Assert.Equal(receipt.TransferId, audit.Entries[0].TransferId);
        Assert.Equal("succeeded", receipt.Execution.Status);
        Assert.Contains("transferFunds.debit", receipt.Execution.Effects);
        Assert.Contains("transferFunds.credit", receipt.Execution.Effects);
    }

    [Fact]
    public void Available_funds_uses_pending_transactions_and_regulatory_hold_not_raw_balance()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m) with
        {
            PendingTransactions = 300m, RegulatoryHold = 250m
        };
        var destination = NewAccount(101, 0m, dailyLimit: 5000m);
        var service = new TransferFundsService(NewStore(source, destination), new OwnershipAuthorization(),
            new InMemoryBankAuditSink());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 451m, "k-2")));

        Assert.Contains("available funds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Frozen_destination_is_rejected_before_any_balance_changes()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 250m, dailyLimit: 5000m) with { IsFrozen = true };
        var store = NewStore(source, destination);
        var service = new TransferFundsService(store, new OwnershipAuthorization(), new InMemoryBankAuditSink());

        Assert.Throws<InvalidOperationException>(() =>
            service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 100m, "k-3")));
        Assert.Equal(1000m, store.Get(source.Id).Balance);
        Assert.Equal(250m, store.Get(destination.Id).Balance);
    }

    [Fact]
    public void Daily_limit_is_enforced_using_prior_transfers()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 500m) with { DailyTransferred = 450m };
        var destination = NewAccount(101, 0m, dailyLimit: 500m);
        var service = new TransferFundsService(NewStore(source, destination), new OwnershipAuthorization(),
            new InMemoryBankAuditSink());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 51m, "k-4")));
        Assert.Contains("daily limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cross_tenant_request_is_rejected()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 250m, dailyLimit: 5000m) with { TenantId = 99 };
        var store = NewStore(source, destination);
        var service = new TransferFundsService(store, new OwnershipAuthorization(), new InMemoryBankAuditSink());

        Assert.Throws<InvalidOperationException>(() =>
            service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 100m, "k-5")));
        Assert.Equal(1000m, store.Get(source.Id).Balance);
    }

    [Fact]
    public void Authorization_is_rechecked_at_execution_boundary()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 250m, dailyLimit: 5000m);
        var store = NewStore(source, destination);
        var authorization = new MutableAuthorization { Allowed = false };
        var service = new TransferFundsService(store, authorization, new InMemoryBankAuditSink());

        Assert.Throws<SemanticAuthorizationException>(() =>
            service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 100m, "k-6")));
        Assert.Equal(1000m, store.Get(source.Id).Balance);
    }

    [Fact]
    public void Replaying_same_idempotency_key_returns_original_result_without_double_debit()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 250m, dailyLimit: 5000m);
        var store = NewStore(source, destination);
        var audit = new InMemoryBankAuditSink();
        var service = new TransferFundsService(store, new OwnershipAuthorization(), audit);
        var command = new TransferFundsCommand(source.Id, destination.Id, 400m, "replay-key");

        var first = service.Execute(_actor, _tenant, command);
        var second = service.Execute(_actor, _tenant, command);

        Assert.False(first.Replay);
        Assert.True(second.Replay);
        Assert.Equal(first.TransferId, second.TransferId);
        Assert.Equal(600m, store.Get(source.Id).Balance);
        Assert.Equal(650m, store.Get(destination.Id).Balance);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public void Reusing_idempotency_key_for_different_amount_is_rejected()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 0m, dailyLimit: 5000m);
        var service = new TransferFundsService(NewStore(source, destination), new OwnershipAuthorization(),
            new InMemoryBankAuditSink());
        service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 100m, "bound-key"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.Execute(_actor, _tenant, new TransferFundsCommand(source.Id, destination.Id, 101m, "bound-key")));

        Assert.Contains("bound to a different", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replaying_idempotency_key_still_requires_current_authorization()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 0m, dailyLimit: 5000m);
        var authorization = new MutableAuthorization { Allowed = true };
        var service =
            new TransferFundsService(NewStore(source, destination), authorization, new InMemoryBankAuditSink());
        var command = new TransferFundsCommand(source.Id, destination.Id, 100m, "auth-replay-key");
        service.Execute(_actor, _tenant, command);
        authorization.Allowed = false;

        Assert.Throws<SemanticAuthorizationException>(() => service.Execute(_actor, _tenant, command));
    }

    [Fact]
    public async Task Concurrent_same_key_is_applied_once()
    {
        var source = NewAccount(100, 1000m, dailyLimit: 5000m);
        var destination = NewAccount(101, 0m, dailyLimit: 5000m);
        var store = NewStore(source, destination);
        var audit = new InMemoryBankAuditSink();
        var service = new TransferFundsService(store, new OwnershipAuthorization(), audit);
        var command = new TransferFundsCommand(source.Id, destination.Id, 400m, "concurrent-key");

        var receipts = await Task.WhenAll(
            Task.Run(() => service.Execute(_actor, _tenant, command)),
            Task.Run(() => service.Execute(_actor, _tenant, command)),
            Task.Run(() => service.Execute(_actor, _tenant, command)));

        Assert.Single(receipts.Select(x => x.TransferId).Distinct());
        Assert.Equal(600m, store.Get(source.Id).Balance);
        Assert.Equal(400m, store.Get(destination.Id).Balance);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public void Capability_exposes_the_business_definition_as_constraints()
    {
        var capability = TransferFundsService.DescribeCapability();
        Assert.Equal(TransferFundsService.CapabilityId, capability.Id);
        Assert.Equal("transferFunds", capability.Operation);
        Assert.True(capability.HasSideEffects);
        Assert.True(capability.IsIdempotent);
        Assert.Contains(capability.Constraints, x => x.Name == "available-funds");
        Assert.Contains(capability.Constraints, x => x.Name == "daily-limit");
        Assert.Contains(capability.Constraints, x => x.Name == "atomicity");
        Assert.Contains(capability.Effects, x => x.Name == "transfer.audit");
    }

    private BankAccountStore NewStore(params BankAccount[] accounts)
    {
        var store = new BankAccountStore();
        foreach (var account in accounts) store.Add(account);
        return store;
    }

    private BankAccount NewAccount(int suffix, decimal balance, decimal dailyLimit) => new(
        Guid.Parse($"00000000-0000-0000-0000-{suffix:000000000000}"),
        _tenant,
        _actor,
        balance,
        0m,
        0m,
        0m,
        dailyLimit,
        false);

    private sealed class MutableAuthorization : IBankAuthorization
    {
        public bool Allowed { get; set; }
        public bool CanTransfer(Guid actorId, BankAccount source, BankAccount destination) => Allowed;
    }
}