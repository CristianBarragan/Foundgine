using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

/// <summary>
/// Locks the P0.3 authorization contract at the semantic-to-plan boundary.
/// These tests intentionally avoid SQL and GraphQL so the invariants remain
/// provider-independent.
/// </summary>
public sealed class AuthorizationInvariantTests
{
    [Fact]
    public void Denied_root_never_produces_an_execution_plan()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1)]);

        var exception = Assert.Throws<SemanticAuthorizationException>(() =>
            new SemanticAuthorizer(new DenyRootPolicy()).Authorize(graph));

        Assert.Contains("Access denied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Denied_field_cannot_appear_in_the_execution_plan()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1), new FieldId(2)]);

        var authorized = new SemanticAuthorizer(new DenyFieldPolicy()).Authorize(graph);
        var plan = new Planner().Plan(authorized);

        Assert.Equal([new FieldId(1)], plan.Root.Fields);
        Assert.DoesNotContain(new FieldId(2), plan.Root.Fields);
    }

    [Fact]
    public void Denied_relationship_cannot_appear_as_an_execution_traversal()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        graph.Add(new EntityId(2), new RelationshipId(10), root, [new FieldId(1)]);

        var authorized = new SemanticAuthorizer(new DenyRelationshipPolicy()).Authorize(graph);
        var plan = new Planner().Plan(authorized);

        Assert.Empty(plan.Root.Children);
    }

    [Fact]
    public void Conditional_authorization_is_preserved_in_the_execution_plan()
    {
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1)], predicate);

        var authorized = new SemanticAuthorizer(new ConditionalPolicy(predicate)).Authorize(graph);
        var plan = new Planner().Plan(authorized);

        Assert.Same(predicate, plan.Root.Authorization);
    }

    private sealed class DenyRootPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => false;
    }

    private sealed class DenyFieldPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) => fieldId != new FieldId(2);
    }

    private sealed class DenyRelationshipPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => false;
    }

    private sealed class ConditionalPolicy(AuthorizationPredicate predicate) : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read ? predicate : null;
    }
}
