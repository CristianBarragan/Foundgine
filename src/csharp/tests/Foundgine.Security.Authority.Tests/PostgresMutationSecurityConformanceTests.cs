using Foundgine.HighAssurance.Postgres;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

public sealed class PostgresMutationSecurityConformanceTests
{
    [Fact]
    public void TransferFunds_declares_all_high_assurance_invariants()
    {
        var contract = PostgresMutationSecurityConformance.TransferFunds;

        Assert.Contains("tenant.isolation", contract.RequiredInvariants);
        Assert.Contains("authorization.runtime", contract.RequiredInvariants);
        Assert.Contains("authorization.ownership", contract.RequiredInvariants);
        Assert.Contains("mutation.daily-limit", contract.RequiredInvariants);
        Assert.Contains("mutation.atomic", contract.RequiredInvariants);
        Assert.Contains("mutation.atomic.row-locking", contract.RequiredInvariants);
        Assert.Contains("mutation.idempotency", contract.RequiredInvariants);
        Assert.Contains("mutation.replay-protection", contract.RequiredInvariants);
        Assert.Contains("evidence.audit", contract.RequiredInvariants);
        Assert.Contains("evidence.execution-receipt", contract.RequiredInvariants);
        Assert.True(contract.IsSatisfied);
        Assert.Empty(contract.MissingRequirements());
    }

    [Fact]
    public void TransferFunds_contract_is_known_to_the_security_registry()
    {
        PostgresMutationSecurityConformanceGate.EnsureKnownInvariants();
    }

    [Theory]
    [InlineData("UsesSingleTransaction")]
    [InlineData("LocksMutationRowsDeterministically")]
    [InlineData("RevalidatesAuthorizationAtExecution")]
    [InlineData("SerializesIdempotencyKeys")]
    [InlineData("PersistsIdempotencyInsideTransaction")]
    [InlineData("PersistsAuditInsideTransaction")]
    [InlineData("EmitsExecutionReceipt")]
    [InlineData("EnforcesOwnership")]
    [InlineData("EnforcesDailyLimit")]
    public void Every_high_assurance_provider_obligation_fails_closed_when_removed(string obligation)
    {
        var contract = PostgresMutationSecurityConformance.TransferFunds;
        contract = obligation switch
        {
            "UsesSingleTransaction" => contract with { UsesSingleTransaction = false },
            "LocksMutationRowsDeterministically" => contract with { LocksMutationRowsDeterministically = false },
            "RevalidatesAuthorizationAtExecution" => contract with { RevalidatesAuthorizationAtExecution = false },
            "SerializesIdempotencyKeys" => contract with { SerializesIdempotencyKeys = false },
            "PersistsIdempotencyInsideTransaction" => contract with { PersistsIdempotencyInsideTransaction = false },
            "PersistsAuditInsideTransaction" => contract with { PersistsAuditInsideTransaction = false },
            "EmitsExecutionReceipt" => contract with { EmitsExecutionReceipt = false },
            "EnforcesOwnership" => contract with { EnforcesOwnership = false },
            "EnforcesDailyLimit" => contract with { EnforcesDailyLimit = false },
            _ => throw new ArgumentOutOfRangeException(nameof(obligation))
        };

        Assert.False(contract.IsSatisfied);
        Assert.NotEmpty(contract.MissingRequirements());
        Assert.Throws<InvalidOperationException>(contract.EnsureSatisfied);
    }

    [Fact]
    public void Incomplete_provider_contract_fails_closed()
    {
        var incomplete = PostgresMutationSecurityConformance.TransferFunds with
        {
            PersistsAuditInsideTransaction = false
        };

        var missing = incomplete.MissingRequirements();
        Assert.Contains("evidence.audit", missing);
        Assert.False(incomplete.IsSatisfied);
        Assert.Throws<InvalidOperationException>(incomplete.EnsureSatisfied);
    }
}
