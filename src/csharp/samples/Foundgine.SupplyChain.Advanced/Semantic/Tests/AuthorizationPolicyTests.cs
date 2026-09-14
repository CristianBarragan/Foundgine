using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class AuthorizationPolicyTests
{
    [Fact]
    public void Entity_field_relationship_conditional_write_and_named_operation_policies_are_distinct()
    {
        var analyst = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.Analyst);
        var operatorPolicy = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.WarehouseOperator);

        Assert.True(analyst.CanAccessEntity(SupplyChainSemanticModel.Product));
        Assert.True(analyst.CanAccessEntity(SupplyChainSemanticModel.ComplianceIncident));
        Assert.False(analyst.CanAccessField(SupplyChainSemanticModel.InventoryLot,
            SupplyChainAuthorization.FieldIds.InventoryQuarantined));
        Assert.False(operatorPolicy.CanAccessRelationship(SupplyChainSemanticModel.Supplier,
            SupplyChainAuthorization.RelationshipIds.SupplierIncidents));
        Assert.NotNull(analyst.GetPredicate(SupplyChainSemanticModel.Warehouse, AuthorizationOperation.Read));
        Assert.False(analyst.GetEntityAccess(SupplyChainSemanticModel.InventoryLot, AuthorizationOperation.Write)
            .IsAllowed);
        Assert.False(operatorPolicy.GetEntityAccess(SupplyChainSemanticModel.InventoryLot, AuthorizationOperation.Write,
            new AuthorizationOperationName("inventory.reconcile")).IsAllowed);
        Assert.True(operatorPolicy.GetEntityAccess(SupplyChainSemanticModel.InventoryLot, AuthorizationOperation.Write,
            new AuthorizationOperationName("update")).IsAllowed);
    }
}

/// <summary>
/// Unit coverage for <see>
///     <cref>ClientClaimsValidator</cref>
/// </see>
/// in isolation, plus
/// coverage of how <see cref="ConfiguredSemanticAuthorizationPolicy"/> consumes only
/// the validated, accepted claims that come out of it. These are the same
/// scenarios the MCP adversarial client exercises end-to-end; the unit tests
/// pin the behavior at the policy layer so a regression fails fast in CI
/// without needing the MCP server running.
/// </summary>
public sealed class ClientClaimsValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("role")]
    [InlineData("tenant")]
    [InlineData("tenantId")]
    [InlineData("actor")]
    [InlineData("isAdmin")]
    [InlineData("permissions")]
    public void Identity_claims_are_never_accepted_and_fail_the_whole_request_closed(string identityKey)
    {
        var raw = new Dictionary<string, string> { [identityKey] = "SupplyChainManager" };

        var result = ClientClaimsValidator.Validate(raw, Now);

        Assert.True(result.IsSpoofingAttempt);
        Assert.Empty(result.Accepted);
        Assert.Contains(result.Rejected, r => r.Key.Equals(identityKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Identity_claim_is_rejected_even_when_its_value_matches_the_real_identity()
    {
        // The point is not "is this value plausible" — it is "this channel is
        // never trusted for identity", regardless of whether the value would
        // have been correct anyway.
        var raw = new Dictionary<string, string> { ["tenant"] = "tenant-a" };

        var result = ClientClaimsValidator.Validate(raw, Now);

        Assert.True(result.IsSpoofingAttempt);
    }

    [Fact]
    public void Unrecognized_claim_keys_are_dropped_individually_without_failing_the_request()
    {
        var raw = new Dictionary<string, string> { ["favorite_color"] = "blue", ["scope"] = "read-only" };

        var result = ClientClaimsValidator.Validate(raw, Now);

        Assert.False(result.IsSpoofingAttempt);
        Assert.True(result.Accepted.ContainsKey("scope"));
        Assert.Contains(result.Rejected, r => r.Key == "favorite_color");
    }

    [Theory]
    [InlineData("scope", "full-access")]
    [InlineData("warehouse", "-3")]
    [InlineData("warehouse", "not-a-number")]
    [InlineData("max_rows", "999999")]
    [InlineData("reason", "short")]
    [InlineData("change_ticket", "TICKET-1")]
    [InlineData("not_after", "not-a-date")]
    public void Malformed_claim_values_are_rejected_individually(string key, string value)
    {
        var raw = new Dictionary<string, string> { [key] = value };

        var result = ClientClaimsValidator.Validate(raw, Now);

        Assert.False(result.IsSpoofingAttempt);
        Assert.DoesNotContain(key, result.Accepted.Keys);
        Assert.Contains(result.Rejected, r => r.Key == key);
    }

    [Fact]
    public void Evidence_claims_paired_with_an_expired_not_after_are_rejected_as_stale()
    {
        var raw = new Dictionary<string, string>
        {
            ["reason"] = "Quarterly cycle count discrepancy",
            ["change_ticket"] = "CHG-4821",
            ["not_after"] = "2020-01-01T00:00:00Z"
        };

        var result = ClientClaimsValidator.Validate(raw, Now);

        Assert.False(result.IsSpoofingAttempt);
        Assert.DoesNotContain("reason", result.Accepted.Keys);
        Assert.DoesNotContain("change_ticket", result.Accepted.Keys);
        Assert.DoesNotContain("not_after", result.Accepted.Keys);
    }

    [Fact]
    public void Well_formed_evidence_without_expiry_is_accepted()
    {
        var raw = new Dictionary<string, string>
        {
            ["reason"] = "Quarterly cycle count discrepancy",
            ["change_ticket"] = "CHG-4821"
        };

        var result = ClientClaimsValidator.Validate(raw, Now);

        Assert.False(result.IsSpoofingAttempt);
        Assert.Equal("CHG-4821", result.Accepted["change_ticket"]);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Self_imposed_read_only_scope_claim_narrows_a_managers_write_access()
    {
        var raw = new Dictionary<string, string> { ["scope"] = "read-only" };
        var claims = ClientClaimsValidator.Validate(raw, Now);
        var manager = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.SupplyChainManager, claims.Accepted);
        var managerWithoutClaim = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.SupplyChainManager);

        Assert.False(manager.CanWriteEntity(SupplyChainSemanticModel.InventoryLot));
        Assert.True(managerWithoutClaim.CanWriteEntity(SupplyChainSemanticModel.InventoryLot));
    }

    [Fact]
    public void Warehouse_scope_claim_is_anded_onto_the_existing_tenant_predicate_not_ored()
    {
        var raw = new Dictionary<string, string> { ["warehouse"] = "12" };
        var claims = ClientClaimsValidator.Validate(raw, Now);
        var policy = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.Analyst, claims.Accepted);

        var predicate = policy.GetPredicate(SupplyChainSemanticModel.Warehouse, AuthorizationOperation.Read);

        Assert.NotNull(predicate);
        Assert.Equal(AuthorizationPredicateKind.And, predicate!.Kind);
    }

    [Fact]
    public void Reconcile_requires_manager_role_and_valid_evidence_claims_together()
    {
        var noEvidence = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.SupplyChainManager);
        Assert.False(noEvidence
            .GetEntityAccess(SupplyChainSemanticModel.InventoryLot, AuthorizationOperation.Write,
                new AuthorizationOperationName("inventory.reconcile"))
            .IsAllowed);

        var evidenceRaw = new Dictionary<string, string>
        {
            ["reason"] = "Quarterly cycle count discrepancy",
            ["change_ticket"] = "CHG-4821"
        };
        var validatedEvidence = ClientClaimsValidator.Validate(evidenceRaw, Now);
        var withEvidence = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.SupplyChainManager,
            validatedEvidence.Accepted);
        Assert.True(withEvidence
            .GetEntityAccess(SupplyChainSemanticModel.InventoryLot, AuthorizationOperation.Write,
                new AuthorizationOperationName("inventory.reconcile"))
            .IsAllowed);

        // Valid evidence still cannot substitute for role: an operator with
        // the exact same claims remains denied.
        var operatorWithEvidence =
            SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.WarehouseOperator, validatedEvidence.Accepted);
        Assert.False(operatorWithEvidence
            .GetEntityAccess(SupplyChainSemanticModel.InventoryLot, AuthorizationOperation.Write,
                new AuthorizationOperationName("inventory.reconcile"))
            .IsAllowed);
    }
}