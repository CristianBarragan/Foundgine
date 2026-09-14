using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.Security.Tests;

/// <summary>
/// Deterministic semantic-security fuzz harness. The generator produces hostile
/// graph shapes and the assertions verify that authorization invariants survive
/// the semantic-to-plan boundary without involving SQL, GraphQL, or a provider.
/// </summary>
public sealed class SemanticSecurityFuzzHarnessTests
{
    private const int Seed = 0x5F0A9;
    private const int Cases = 1000;

    [Fact]
    public void Generated_hostile_graphs_never_escape_authorization_boundary()
    {
        var random = new Random(Seed);
        var policy = new FuzzPolicy();
        var authorizer = new SemanticAuthorizer(policy);
        var planner = new Planner();

        for (var caseIndex = 0; caseIndex < Cases; caseIndex++)
        {
            var graph = GenerateGraph(random);
            SemanticGraph authorized;

            try
            {
                authorized = authorizer.Authorize(graph);
            }
            catch (SemanticAuthorizationException)
            {
                // A denied root is a valid and safe outcome.
                Assert.False(policy.CanAccessEntity(graph.Nodes[0].EntityId));
                continue;
            }

            var plan = planner.Plan(authorized);
            AssertAuthorizedPlan(plan.Root, policy, caseIndex, "root");
        }
    }

    [Fact]
    public void Generated_authorization_predicates_survive_planning()
    {
        var random = new Random(Seed ^ 0x13579);
        var policy = new PredicatePolicy();
        var authorizer = new SemanticAuthorizer(policy);
        var planner = new Planner();

        for (var caseIndex = 0; caseIndex < 250; caseIndex++)
        {
            var graph = GenerateGraph(random, forceRootAllowed: true);
            var authorized = authorizer.Authorize(graph);
            var plan = planner.Plan(authorized);

            Assert.NotNull(plan.Root.Authorization);
            Assert.Equal(policy.Predicate, plan.Root.Authorization);
        }
    }

    [Fact]
    public void Fuzz_harness_is_reproducible()
    {
        var first = GenerateFingerprints(Seed, 100);
        var second = GenerateFingerprints(Seed, 100);

        Assert.Equal(first, second);
    }

    private static IReadOnlyList<string> GenerateFingerprints(int seed, int count)
    {
        var random = new Random(seed);
        var policy = new FuzzPolicy();
        var authorizer = new SemanticAuthorizer(policy);
        var planner = new Planner();
        var fingerprints = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var graph = GenerateGraph(random, forceRootAllowed: true);
            var authorized = authorizer.Authorize(graph);
            fingerprints.Add(SemanticPlanFingerprint.Create(planner.Plan(authorized)));
        }

        return fingerprints;
    }

    private static SemanticGraph GenerateGraph(Random random, bool forceRootAllowed = false)
    {
        var graph = new SemanticGraph();
        var rootEntity = forceRootAllowed ? new EntityId(2) : new EntityId((ushort)random.Next(1, 5));
        var root = graph.AddRoot(rootEntity, RandomFields(random));
        var current = new List<SemanticGraphNode> { root };

        var depth = random.Next(1, 5);
        for (var level = 0; level < depth; level++)
        {
            var next = new List<SemanticGraphNode>();
            foreach (var parent in current)
            {
                var childCount = random.Next(0, 3);
                for (var childIndex = 0; childIndex < childCount; childIndex++)
                {
                    var entity = new EntityId((ushort)random.Next(1, 5));
                    var relationship = new RelationshipId((ushort)random.Next(10, 15));
                    next.Add(graph.Add(entity, relationship, parent, RandomFields(random)));
                }
            }

            current = next;
            if (current.Count == 0)
                break;
        }

        return graph;
    }

    private static IReadOnlyList<FieldId> RandomFields(Random random)
    {
        var count = random.Next(1, 5);
        var fields = new HashSet<FieldId>();
        while (fields.Count < count)
            fields.Add(new FieldId((ushort)random.Next(1, 7)));
        return fields.ToArray();
    }

    private static void AssertAuthorizedPlan(
        SemanticPlanNode node,
        FuzzPolicy policy,
        int caseIndex,
        string path)
    {
        Assert.True(
            policy.CanAccessEntity(node.EntityId),
            $"case={caseIndex} path={path}: denied entity reached plan: {node.EntityId}");

        foreach (var field in node.Fields)
        {
            Assert.True(
                policy.CanAccessField(node.EntityId, field),
                $"case={caseIndex} path={path}: denied field reached plan: {field}");
        }

        if (node.ViaRelationship is { } relationship)
        {
            Assert.True(
                policy.CanAccessRelationship(new EntityId(1), relationship),
                $"case={caseIndex} path={path}: denied relationship reached plan: {relationship}");
        }

        for (var i = 0; i < node.Children.Count; i++)
            AssertAuthorizedPlan(node.Children[i], policy, caseIndex, $"{path}/{i}");
    }

    private sealed class FuzzPolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessEntity(EntityId entityId) => entityId.Value % 2 == 0;
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) => fieldId.Value % 2 == 0;

        public override bool CanAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) =>
            relationshipId.Value % 2 == 0;
    }

    private sealed class PredicatePolicy : AllowAllSemanticAuthorizationPolicy
    {
        public AuthorizationPredicate Predicate { get; } = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ResourceParameter("resource"), "TenantId"),
            AuthorizationPredicate.Member(
                AuthorizationPredicate.ContextParameter("user"), "TenantId"));

        public override AuthorizationPredicate? GetPredicate(
            EntityId entityId,
            AuthorizationOperation operation) =>
            operation == AuthorizationOperation.Read ? Predicate : null;
    }
}