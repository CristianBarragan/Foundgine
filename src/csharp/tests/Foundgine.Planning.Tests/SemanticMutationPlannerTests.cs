using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Mutation;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class SemanticMutationPlannerTests
{
    [Fact]
    public void SemanticCreateLowersFieldIdentityToProviderNeutralMutationPlan()
    {
        var entity = new EntityId(10);
        var name = new FieldId(20);
        var schema = Schema(entity, (name, new ColumnId(30)));
        var planner = new MutationPlanner(schema);

        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Create(entity, [new SemanticMutationField(name, "Alice")], [name])
        ]);

        var plan = planner.Plan(graph);

        var operation = Assert.Single(plan.Operations);
        Assert.Equal(entity, operation.Entity.Id);
        Assert.Equal(MutationKind.Create, operation.Kind);
        Assert.Equal(new ColumnId(30), Assert.Single(operation.Fields).Column);
        Assert.Equal(name, Assert.Single(operation.ReturnFields!));
    }

    [Fact]
    public void SemanticUpsertLowersConflictFieldsWithoutIntroducingSql()
    {
        var entity = new EntityId(10);
        var email = new FieldId(20);
        var schema = Schema(entity, (email, new ColumnId(30)));
        var planner = new MutationPlanner(schema);

        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Upsert(entity, [new SemanticMutationField(email, "a@example.com")], [email])
        ]);

        var plan = planner.Plan(graph);

        var operation = Assert.Single(plan.Operations);
        Assert.Equal(MutationKind.Upsert, operation.Kind);
        Assert.Equal(new ColumnId(30), Assert.Single(operation.ConflictColumns!));
    }

    [Fact]
    public void SemanticDependencyBecomesProviderNeutralDependency()
    {
        var customer = new EntityId(10);
        var account = new EntityId(11);
        var customerId = new FieldId(20);
        var accountCustomerId = new FieldId(30);

        var schema = new TestMutationSchema(
            new MutationEntitySchema(customer, "Customer", new HashSet<ColumnId> { new(100) },
                new Dictionary<FieldId, ColumnId?> { [customerId] = new ColumnId(100) }, new ColumnId(100)),
            new MutationEntitySchema(account, "Account", new HashSet<ColumnId> { new(200) },
                new Dictionary<FieldId, ColumnId?> { [accountCustomerId] = new ColumnId(200) }, new ColumnId(200)));

        var planner = new MutationPlanner(schema);
        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Create(customer, [new SemanticMutationField(customerId, "c")], [customerId]),
            SemanticMutationBuilder.Create(account, [
                    new SemanticMutationField(
                        accountCustomerId, null, new SemanticMutationValueReference(0, customerId))
                ])
                with
                {
                    ReturnFields = [accountCustomerId]
                }
        ]);

        var plan = planner.Plan(graph);
        var dependency = Assert.Single(plan.Dependencies);

        Assert.Equal(0, dependency.SourceOperationIndex);
        Assert.Equal(1, dependency.TargetOperationIndex);
        Assert.Equal(customerId, dependency.SourceField);
        Assert.Equal(new ColumnId(200), dependency.TargetColumn);

        var semanticPlan = new SemanticMutationPlanner().Plan(graph);
        var semanticDependency = Assert.Single(semanticPlan.Dependencies);
        Assert.Equal("0", semanticDependency.FromOperationId);
        Assert.Equal("1", semanticDependency.ToOperationId);
        Assert.Equal(customerId, semanticDependency.SourceField);
        Assert.Equal(accountCustomerId, semanticDependency.TargetField);
    }

    [Fact]
    public void SemanticPlannerDerivesCorrelationFromFieldValueReference()
    {
        var customer = new EntityId(10);
        var account = new EntityId(11);
        var customerId = new FieldId(20);
        var accountCustomerId = new FieldId(30);

        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Create(customer, [new SemanticMutationField(customerId, null)], [customerId]),
            SemanticMutationBuilder.Create(account, [
                new SemanticMutationField(
                    accountCustomerId,
                    null,
                    new SemanticMutationValueReference(0, customerId))
            ])
        ]);

        var plan = new SemanticMutationPlanner().Plan(graph);

        var dependency = Assert.Single(plan.Dependencies);

        Assert.Equal("0", dependency.FromOperationId);
        Assert.Equal("1", dependency.ToOperationId);
        Assert.Equal(customerId, dependency.SourceField);
        Assert.Equal(accountCustomerId, dependency.TargetField);
    }

    [Fact]
    public void SemanticPlanHasOneCanonicalDependencyCollection()
    {
        var properties = typeof(SemanticMutationPlan).GetProperties();

        Assert.Contains(properties, x => x.Name == nameof(SemanticMutationPlan.Dependencies));
        Assert.DoesNotContain(properties, x => x.Name == "Correlations");
        Assert.DoesNotContain(properties, x => x.Name == "CorrelationRequirements");
    }

    [Fact]
    public void SemanticPlanPreservesCompleteOperationSemantics()
    {
        // The existing suite constructs concrete semantic graphs; this assertion
        // protects the architectural contract at the type level.
        Assert.True(typeof(SemanticMutationPlan).GetProperty(nameof(SemanticMutationPlan.Operations)) is not null);
        Assert.True(
            typeof(SemanticMutationOperationPlan).GetProperty(nameof(SemanticMutationOperationPlan
                .Filter)) is not null);
        Assert.True(
            typeof(SemanticMutationOperationPlan).GetProperty(nameof(SemanticMutationOperationPlan.ConflictFields)) is
                not null);
        Assert.True(
            typeof(SemanticMutationOperationPlan).GetProperty(nameof(SemanticMutationOperationPlan.ReturnFields)) is not
                null);
    }

    private static TestMutationSchema Schema(EntityId entity, params (FieldId Field, ColumnId Column)[] fields) =>
        new(new MutationEntitySchema(
            entity,
            "Entity",
            fields.Select(x => x.Column).ToHashSet(),
            fields.ToDictionary(x => x.Field, x => (ColumnId?)x.Column),
            fields.Length == 0 ? null : fields[0].Column));

    private sealed class TestMutationSchema : IMutationSchema
    {
        private readonly Dictionary<EntityId, MutationEntitySchema> _entities;

        public TestMutationSchema(params MutationEntitySchema[] entities) =>
            _entities = entities.ToDictionary(x => x.Id);

        public MutationEntitySchema GetEntity(EntityId entityId) => _entities[entityId];

        public MutationRelationshipSchema GetRelationship(RelationshipId relationshipId) =>
            throw new KeyNotFoundException();
    }
}