using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

/// <summary>
/// Provider-independent adversarial checks for the semantic security boundary.
/// These are intentionally small: the goal is to lock the invariants that must
/// hold before SQL/GraphQL/MCP execution is allowed to occur.
/// </summary>
public sealed class AdversarialSemanticSecurityTests
{
    [Fact]
    public void Cross_tenant_predicate_is_preserved_and_cannot_be_dropped_during_discovery()
    {
        var model = Model();
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var contract = SemanticCapabilityContractDiscovery.Describe(
            model, new TenantPolicy(predicate));

        var capability = Assert.Single(contract.Capabilities, x => x.Id == "Customer.read");
        Assert.Equal(AuthorizationAccess.Conditional, capability.Access.Access);
        Assert.Same(predicate, capability.Access.Predicate);
    }

    [Fact]
    public void Hidden_field_is_not_advertised_as_an_agent_capability()
    {
        var model = Model();
        var contract = SemanticCapabilityContractDiscovery.Describe(
            model, new HiddenFieldPolicy());

        var read = Assert.Single(contract.Capabilities, x => x.Id == "Customer.read");
        Assert.Contains("Name", read.Fields);
        Assert.DoesNotContain("Balance", read.Fields);
    }

    [Fact]
    public void Unauthorized_relationship_traversal_is_not_advertised()
    {
        var model = ModelWithAccount();
        var contract = SemanticCapabilityContractDiscovery.Describe(
            model, new DenyAccountTraversalPolicy());

        Assert.DoesNotContain(
            contract.Capabilities,
            x => x.Id == "Customer.accounts.traverse");
    }

    [Fact]
    public void Capability_contract_marks_writes_as_side_effecting_and_non_idempotent_when_appropriate()
    {
        var contract = SemanticCapabilityContractDiscovery.Describe(
            Model(), new AllowAllSemanticAuthorizationPolicy());

        var create = Assert.Single(contract.Capabilities, x => x.Id == "Customer.create");
        Assert.True(create.HasSideEffects);
        Assert.False(create.IsIdempotent);
        Assert.Contains(create.Constraints, x => x.Name == "writable-fields");
    }

    private static SemanticModel Model() =>
        new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Field(new FieldId(3), "Balance", typeof(decimal))
                .Field(new FieldId(4), "TenantId", typeof(int)))
            .Build();

    private static SemanticModel ModelWithAccount() =>
        new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(10), "accounts", new EntityId(2), RelationshipCardinality.Many))
            .Entity(new EntityId(2), "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Balance", typeof(decimal)))
            .Build();

    private sealed class TenantPolicy(AuthorizationPredicate predicate) : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read ? predicate : null;
    }

    private sealed class HiddenFieldPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) => fieldId != new FieldId(3);
    }

    private sealed class DenyAccountTraversalPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) =>
            relationshipId != new RelationshipId(10);
    }
}
