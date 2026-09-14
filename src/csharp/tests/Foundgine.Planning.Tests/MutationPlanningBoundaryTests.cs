using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Authorization;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class MutationPlanningBoundaryTests
{
    [Fact]
    public void Planner_Consumes_Narrow_MutationSchema_Without_Metadata()
    {
        var entity = new MutationEntitySchema(
            new EntityId(1),
            "Customer",
            new HashSet<ColumnId> { new(1), new(2) },
            new Dictionary<FieldId, ColumnId?>
            {
                [new FieldId(1)] = new ColumnId(1),
                [new FieldId(2)] = new ColumnId(2)
            },
            new ColumnId(1));

        var schema = new TestMutationSchema(entity);
        var intent = new MutationIntent(
            entity.Id,
            MutationKind.Create,
            [new MutationFieldValue(new ColumnId(2), "Ada")],
            ReturnFields: [new FieldId(1), new FieldId(2)]);

        var plan = new MutationPlanner(schema).Plan(intent);

        Assert.Equal(entity.Id, plan.Operations[0].Entity.Id);
        Assert.Equal("Customer", plan.Operations[0].Entity.Name);
        Assert.Equal([new FieldId(1), new FieldId(2)], plan.Operations[0].ReturnFields);
    }

    [Fact]
    public void Planner_Uses_MutationSchema_For_Nested_Relationship_Mapping()
    {
        var customer = new MutationEntitySchema(
            new EntityId(1), "Customer",
            new HashSet<ColumnId> { new(1), new(2) },
            new Dictionary<FieldId, ColumnId?>
            {
                [new FieldId(1)] = new ColumnId(1),
                [new FieldId(2)] = new ColumnId(2)
            },
            new ColumnId(1));

        var account = new MutationEntitySchema(
            new EntityId(2), "Account",
            new HashSet<ColumnId> { new(1), new ColumnId(2), new ColumnId(3) },
            new Dictionary<FieldId, ColumnId?>
            {
                [new FieldId(1)] = new ColumnId(1),
                [new FieldId(3)] = new ColumnId(3)
            },
            new ColumnId(1));

        var relationship = new MutationRelationshipSchema(
            new RelationshipId(1), customer.Id, account.Id, "Accounts",
            new ColumnId(1), new ColumnId(2));

        var schema = new TestMutationSchema(customer, account, relationship);
        var nested = new NestedMutationIntent(
            new MutationIntent(customer.Id, MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Ada")],
                ReturnFields: [new FieldId(1)]),
            [
                new NestedMutationChild(
                    relationship.Id,
                    new NestedMutationIntent(
                        new MutationIntent(account.Id, MutationKind.Create,
                            [new MutationFieldValue(new ColumnId(3), "Primary")],
                            ReturnFields: [new FieldId(1)]),
                        []))
            ]);

        var plan = new MutationPlanner(schema).Plan(nested);

        Assert.Equal(2, plan.Operations.Count);
        Assert.Equal(new ColumnId(2), plan.Operations[1].Fields[^1].Column);
        Assert.Single(plan.Dependencies);
    }

    private sealed class TestMutationSchema : IMutationSchema
    {
        private readonly Dictionary<EntityId, MutationEntitySchema> _entities;
        private readonly Dictionary<RelationshipId, MutationRelationshipSchema> _relationships;

        public TestMutationSchema(params object[] items)
        {
            _entities = items.OfType<MutationEntitySchema>().ToDictionary(x => x.Id);
            _relationships = items.OfType<MutationRelationshipSchema>().ToDictionary(x => x.Id);
        }

        public MutationEntitySchema GetEntity(EntityId entityId) => _entities[entityId];

        public MutationRelationshipSchema GetRelationship(RelationshipId relationshipId) =>
            _relationships[relationshipId];
    }
}

public sealed class MutationAuthorizationTests
{
    [Fact]
    public void Mutation_authorizer_rejects_non_writable_field()
    {
        var entity = new MutationEntitySchema(
            new EntityId(1),
            "Employee",
            new HashSet<ColumnId> { new(1), new(2) },
            new Dictionary<FieldId, ColumnId?>
            {
                [new FieldId(1)] = new ColumnId(1),
                [new FieldId(2)] = new ColumnId(2)
            },
            new ColumnId(1));

        var schema = new TestMutationSchema(entity);
        var intent = new MutationIntent(
            entity.Id,
            MutationKind.Update,
            [new MutationFieldValue(new ColumnId(2), "secret")],
            Filter: new Foundgine.Core.Semantic.Query.SemanticFieldFilter(
                new FieldId(1),
                Foundgine.Core.Semantic.Query.SemanticFilterOperator.Eq,
                1),
            ReturnFields: [new FieldId(1)]);
        var plan = new MutationPlanner(schema).Plan(intent);

        var ex = Assert.Throws<Foundgine.Core.Semantic.Authorization.SemanticAuthorizationException>(() =>
            new MutationAuthorizer(schema, new DenyFieldWritePolicy()).Authorize(plan));

        Assert.Contains("write field", ex.Message);
    }

    private sealed class TestMutationSchema : IMutationSchema
    {
        private readonly Dictionary<EntityId, MutationEntitySchema> _entities;
        private readonly Dictionary<RelationshipId, MutationRelationshipSchema> _relationships;

        public TestMutationSchema(params object[] items)
        {
            _entities = items.OfType<MutationEntitySchema>().ToDictionary(x => x.Id);
            _relationships = items.OfType<MutationRelationshipSchema>().ToDictionary(x => x.Id);
        }

        public MutationEntitySchema GetEntity(EntityId entityId) => _entities[entityId];

        public MutationRelationshipSchema GetRelationship(RelationshipId relationshipId) =>
            _relationships[relationshipId];
    }

    private sealed class DenyFieldWritePolicy : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanWriteField(EntityId entityId, FieldId fieldId) =>
            fieldId != new FieldId(2);
    }
}