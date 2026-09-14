using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Tests.Security;

public sealed class SecurityInvariantRegistryTests
{
    [Fact]
    public void Registry_contains_canonical_invariants()
    {
        Assert.Contains(SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id));
        Assert.Contains(SecurityInvariantIds.RuntimeAuthorization,
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id));
        Assert.Contains(SecurityInvariantIds.TenantIsolation,
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id));
        Assert.Contains(SecurityInvariantIds.PlanCacheContextIsolation,
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id));
        Assert.Contains(SecurityInvariantIds.AtomicMutation, SecurityInvariantRegistry.AllInvariants.Select(x => x.Id));
        Assert.Contains(SecurityInvariantIds.ExecutionEvidenceRequired,
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id));
    }

    [Fact]
    public void Generic_mutation_capability_has_minimum_security_invariants()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Order", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Total", typeof(decimal)))
            .Build();

        var contract = SemanticCapabilityContractDiscovery.Describe(model, new AllowAllSemanticAuthorizationPolicy());
        var update = Assert.Single(contract.Capabilities, x => x.Id == "Order.update");

        Assert.Contains(SecurityInvariantIds.AuthorizationRequired, update.EffectiveSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.RuntimeAuthorization, update.EffectiveSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.FieldVisibility, update.EffectiveSecurityInvariants);
        Assert.Contains(SecurityInvariantIds.ParameterizedValues, update.EffectiveSecurityInvariants);
        SecurityInvariantContractValidator.EnsureValid(update);
    }

    [Fact]
    public void Invalid_mutating_capability_is_rejected_as_a_contract_violation()
    {
        var capability = new SemanticCapability(
            "money.transfer", "Transfer", new EntityId(1),
            AuthorizationDecision.Allowed,
            [], [], [new SemanticCapabilityEffect("money.debit", "Debit funds")], [], [])
        {
            Operation = "transfer",
            HasSideEffects = true,
            RequiredSecurityInvariants = [SecurityInvariantIds.ParameterizedValues]
        };

        var errors = SecurityInvariantContractValidator.Validate(capability);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, x => x.Contains(SecurityInvariantIds.RuntimeAuthorization, StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains(SecurityInvariantIds.AuthorizationRequired, StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_invariant_ids_fail_closed()
    {
        var capability = new SemanticCapability(
            "customer.read", "Read Customer", new EntityId(1),
            AuthorizationDecision.Allowed, [], [], [], ["Name"], [])
        {
            RequiredSecurityInvariants = ["security.not-real"]
        };

        var errors = SecurityInvariantContractValidator.Validate(capability);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, x => x.Contains("security.not-real", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains(SecurityInvariantIds.FieldVisibility, StringComparison.Ordinal));
    }

    [Fact]
    public void Contract_validation_is_machine_readable_and_provider_neutral()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var contract = SemanticCapabilityContractDiscovery.Describe(model, new AllowAllSemanticAuthorizationPolicy());
        SecurityInvariantContractValidator.EnsureContractValid(contract);

        var json = System.Text.Json.JsonSerializer.Serialize(contract);
        Assert.Contains(SecurityInvariantIds.AuthorizationRequired, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", json, StringComparison.OrdinalIgnoreCase);
    }
}