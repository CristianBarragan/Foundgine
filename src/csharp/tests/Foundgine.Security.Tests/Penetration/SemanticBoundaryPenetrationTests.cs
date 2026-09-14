using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>
/// Penetration-style tests for the agent -> semantic model -> authorization -> planner boundary.
/// The inputs are intentionally hostile and represent untrusted model/MCP output.
/// </summary>
public sealed class SemanticBoundaryPenetrationTests
{
    [Fact]
    public void Denied_root_entity_cannot_reach_planner()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1)]);

        var authorizer = new SemanticAuthorizer(new DenyAllPolicy());

        Assert.Throws<SemanticAuthorizationException>(() => authorizer.Authorize(graph));
    }

    [Fact]
    public void Denied_field_is_removed_before_planning()
    {
        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1), new FieldId(2)]);

        var authorized = new SemanticAuthorizer(new FieldAllowPolicy()).Authorize(graph);
        var plan = new Planner().Plan(authorized);

        Assert.Equal([new FieldId(2)], plan.Root.Fields);
    }

    [Fact]
    public void Denied_relationship_removes_entire_descendant_subtree()
    {
        var graph = new SemanticGraph();
        var root = graph.AddRoot(new EntityId(1), [new FieldId(1)]);
        var child = graph.Add(new EntityId(2), new RelationshipId(10), root, [new FieldId(2)]);
        graph.Add(new EntityId(3), new RelationshipId(11), child, [new FieldId(3)]);

        var authorized = new SemanticAuthorizer(new RootOnlyPolicy()).Authorize(graph);
        var plan = new Planner().Plan(authorized);

        Assert.Empty(plan.Root.Children);
    }

    [Fact]
    public void Authorization_predicate_survives_semantic_planning()
    {
        var predicate = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [new FieldId(1)], predicate);

        var authorized = new SemanticAuthorizer(new PredicatePolicy(predicate)).Authorize(graph);
        var plan = new Planner().Plan(authorized);

        Assert.Equal(predicate, plan.Root.Authorization);
    }

    [Fact]
    public void Authorization_predicate_changes_the_plan_fingerprint()
    {
        var a = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Constant("1"));
        var b = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Constant("2"));

        var graphA = new SemanticGraph();
        graphA.AddRoot(new EntityId(1), [new FieldId(1)], a);
        var graphB = new SemanticGraph();
        graphB.AddRoot(new EntityId(1), [new FieldId(1)], b);

        var planner = new Planner();
        var planA = planner.Plan(new SemanticAuthorizer(new PredicatePolicy(a)).Authorize(graphA));
        var planB = planner.Plan(new SemanticAuthorizer(new PredicatePolicy(b)).Authorize(graphB));

        Assert.NotEqual(SemanticPlanFingerprint.Create(planA), SemanticPlanFingerprint.Create(planB));
        Assert.NotEqual(SemanticPlanFingerprint.CreateShapeKey(planA), SemanticPlanFingerprint.CreateShapeKey(planB));
    }

    private sealed class DenyAllPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => false;
    }

    private sealed class FieldAllowPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) => fieldId == new FieldId(2);
    }

    private sealed class RootOnlyPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId == new EntityId(1);
        public override bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) => false;
    }

    private sealed class PredicatePolicy(AuthorizationPredicate predicate) : AllowAllSemanticAuthorizationPolicy
    {
        public override AuthorizationPredicate? GetPredicate(EntityId entityId, AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read ? predicate : null;
    }
}
