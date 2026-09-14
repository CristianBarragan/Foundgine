using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

/// <summary>
/// Locks the semantic capability contract used by application and AI adapters.
/// These tests deliberately stay provider- and transport-independent.
/// </summary>
public sealed class SemanticCapabilityContractTests
{
    [Fact]
    public void Capability_discovery_is_deterministically_ordered()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(2), "Zebra", e => e
                .Identity(new FieldId(20), "Id")
                .Field(new FieldId(22), "Name", typeof(string)))
            .Entity(new EntityId(1), "Account", e => e
                .Identity(new FieldId(10), "Id")
                .Field(new FieldId(12), "Name", typeof(string)))
            .Build();

        var capabilities = SemanticAuthorizationCapabilityDiscovery.Describe(
            model,
            new AllowAllSemanticAuthorizationPolicy());

        Assert.Equal(["Account", "Zebra"], capabilities.Entities.Select(x => x.Name));
        Assert.Equal(["Name"], capabilities.Entities[0].Fields.Select(x => x.Name));
    }

    [Fact]
    public void Conditional_authorization_is_described_without_exposing_the_predicate()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "TenantId", typeof(int)))
            .Build();

        var capabilities = SemanticAuthorizationCapabilityDiscovery.Describe(
            model,
            new ConditionalCustomerPolicy());

        var customer = Assert.Single(capabilities.Entities);
        Assert.Equal(AuthorizationAccess.Conditional, customer.Read.Access);
        Assert.Null(customer.Read.Predicate);
    }


    [Fact]
    public void Canonical_contract_contains_read_write_and_traversal_capabilities()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Accounts", new EntityId(2), RelationshipCardinality.Many))
            .Entity(new EntityId(2), "Account", e => e
                .Identity(new FieldId(3), "Id")
                .Field(new FieldId(4), "Balance", typeof(decimal)))
            .Build();

        var contract = SemanticCapabilityContractDiscovery.Describe(
            model,
            new AllowAllSemanticAuthorizationPolicy());

        Assert.Equal(SemanticCapabilityContractDiscovery.CurrentVersion, contract.Version);
        Assert.Contains(contract.Capabilities, x => x.Id == "Customer.read" && x.Fields.Contains("Name"));
        Assert.Contains(contract.Capabilities, x => x.Id == "Customer.write" && x.Fields.Contains("Name"));

        var traversal = Assert.Single(contract.Capabilities, x => x.Id == "Customer.Accounts.traverse");
        Assert.Equal(new EntityId(2), traversal.TargetEntityId);
        Assert.Equal(AuthorizationAccess.Allowed, traversal.Access.Access);
    }

    [Fact]
    public void Canonical_contract_exposes_explicit_mutation_actions_and_semantic_constraints()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Order", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Total", typeof(decimal)))
            .Build();

        var contract = SemanticCapabilityContractDiscovery.Describe(
            model,
            new AllowAllSemanticAuthorizationPolicy());

        var update = Assert.Single(contract.Capabilities, x => x.Id == "Order.update");
        Assert.Equal("update", update.Operation);
        Assert.True(update.HasSideEffects);
        Assert.True(update.IsIdempotent);
        Assert.Contains(update.Constraints, x => x.Name == "target-selection");
        Assert.Contains(update.Constraints, x => x.Name == "writable-fields");
        Assert.Contains(update.Effects, x => x.Name == "data.update");

        var create = Assert.Single(contract.Capabilities, x => x.Id == "Order.create");
        Assert.Equal("create", create.Operation);
        Assert.False(create.IsIdempotent);

        var delete = Assert.Single(contract.Capabilities, x => x.Id == "Order.delete");
        Assert.Equal("delete", delete.Operation);
        Assert.Contains(delete.Constraints, x => x.Name == "target-selection");

        var upsert = Assert.Single(contract.Capabilities, x => x.Id == "Order.upsert");
        Assert.Equal("upsert", upsert.Operation);
        Assert.Contains(upsert.Constraints, x => x.Name == "conflict-key");
    }

    [Fact]
    public void Canonical_contract_preserves_policy_scoped_access()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var contract = SemanticCapabilityContractDiscovery.Describe(
            model,
            new ReadOnlyPolicy());

        var read = Assert.Single(contract.Capabilities, x => x.Id == "Customer.read");
        var write = Assert.Single(contract.Capabilities, x => x.Id == "Customer.write");

        Assert.Equal(AuthorizationAccess.Allowed, read.Access.Access);
        Assert.Equal(AuthorizationAccess.Denied, write.Access.Access);
        Assert.Empty(write.Effects);
    }

    [Fact]
    public void Capability_document_is_machine_serializable_without_provider_types()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

        var capabilities = SemanticAuthorizationCapabilityDiscovery.Describe(
            model,
            new AllowAllSemanticAuthorizationPolicy());

        var json = JsonSerializer.Serialize(capabilities);

        Assert.Contains("Customer", json, StringComparison.Ordinal);
        Assert.Contains("Name", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" FROM ", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReadOnlyPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanWriteEntity(EntityId entityId) => false;
        public override bool CanWriteField(EntityId entityId, FieldId fieldId) => false;
    }

    private sealed class ConditionalCustomerPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            entityId == new EntityId(1) && operation == AuthorizationOperation.Read
                ? AuthorizationPredicate.Equal(
                    AuthorizationPredicate.Member(
                        AuthorizationPredicate.Parameter("customer"), "TenantId"),
                    AuthorizationPredicate.ContextParameter("tenantId"))
                : null;
    }
}

public sealed class SemanticVersioningTests
{
    [Fact]
    public void Version_set_is_stable_for_equivalent_models()
    {
        var first = SemanticVersionSet.For(BuildModel());
        var second = SemanticVersionSet.For(BuildModel());

        Assert.Equal(first.SemanticModelVersion, second.SemanticModelVersion);
        Assert.Equal(1, first.CapabilityContractVersion);
        Assert.Equal(1, first.CapabilityVersion);
        Assert.Equal(1, first.IntentVersion);
        Assert.Equal(1, first.PlanVersion);
    }

    [Fact]
    public void Semantic_model_version_changes_when_topology_changes()
    {
        var first = SemanticVersionSet.For(BuildModel());
        var changed = SemanticVersionSet.For(BuildModel(includeExtraEntity: true));

        Assert.NotEqual(first.SemanticModelVersion, changed.SemanticModelVersion);
    }

    private static SemanticModel BuildModel(bool includeExtraEntity = false)
    {
        var builder = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(1), "Name", typeof(string)));

        if (includeExtraEntity)
            builder.Entity(new EntityId(2), "Account", e => e
                .Identity(new FieldId(2), "Id")
                .Field(new FieldId(2), "Number", typeof(string)));

        return builder.Build();
    }
}
