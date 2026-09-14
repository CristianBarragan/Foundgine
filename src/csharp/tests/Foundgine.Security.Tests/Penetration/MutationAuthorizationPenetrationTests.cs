using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>Hostile mutation-plan tests: hidden fields, unauthorized filters and relationships.</summary>
public sealed class MutationAuthorizationPenetrationTests
{
    [Fact]
    public void Unauthorized_write_field_is_rejected()
    {
        var entity = Entity(1, "Account", (1, 11), (2, 12));
        var schema = new TestSchema(entity);
        var plan = new MutationPlan([
            new MutationOperation(
                entity,
                MutationKind.Update,
                [new MutationFieldValue(new ColumnId(12), "attacker")],
                null)
        ]);

        var exception = Assert.Throws<SemanticAuthorizationException>(() =>
            new MutationAuthorizer(schema, new AllowOnlyFieldPolicy(new FieldId(1))).Authorize(plan));

        Assert.Contains("field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unauthorized_return_field_is_rejected()
    {
        var entity = Entity(1, "Account", (1, 11), (2, 12));
        var schema = new TestSchema(entity);
        var plan = new MutationPlan([
            new MutationOperation(
                entity,
                MutationKind.Update,
                [],
                null,
                ReturnFields: [new FieldId(2)])
        ]);

        Assert.Throws<SemanticAuthorizationException>(() =>
            new MutationAuthorizer(schema, new AllowOnlyFieldPolicy(new FieldId(1))).Authorize(plan));
    }

    [Fact]
    public void Unauthorized_filter_field_is_rejected()
    {
        var entity = Entity(1, "Account", (1, 11), (2, 12));
        var schema = new TestSchema(entity);
        var filter = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.Eq, "victim");
        var plan = new MutationPlan([new MutationOperation(entity, MutationKind.Delete, [], filter)]);

        Assert.Throws<SemanticAuthorizationException>(() =>
            new MutationAuthorizer(schema, new AllowOnlyFieldPolicy(new FieldId(1))).Authorize(plan));
    }

    // SEC-21: a batch cannot smuggle an unauthorized operation past authorization
    // by pairing it with an authorized one. This exercises the exact semantic
    // representation FoundgineMutationEngine authorizes as a whole before any
    // lowering to execution IR occurs (see FoundgineMutationEngine.AuthorizeAndPlan),
    // so a rejection here can never leave part of the batch already applied.
    [Fact]
    public void Batch_with_one_unauthorized_operation_rejects_the_entire_batch_when_unauthorized_op_is_last()
    {
        var entity = Entity(1, "Account", (1, 11), (2, 12));
        var schema = new TestSchema(entity);
        var policy = new AllowOnlyFieldPolicy(new FieldId(1));
        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Update(entity.Id, [new SemanticMutationField(new FieldId(1), "ok")]),
            SemanticMutationBuilder.Update(entity.Id, [new SemanticMutationField(new FieldId(2), "attacker")])
        ]);
        var semanticPlan = new SemanticMutationPlanner().Plan(graph);

        var exception = Assert.Throws<SemanticAuthorizationException>(() =>
            new MutationAuthorizer(schema, policy).Authorize(semanticPlan));

        Assert.Contains("field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Batch_with_one_unauthorized_operation_rejects_the_entire_batch_when_unauthorized_op_is_first()
    {
        var entity = Entity(1, "Account", (1, 11), (2, 12));
        var schema = new TestSchema(entity);
        var policy = new AllowOnlyFieldPolicy(new FieldId(1));
        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Update(entity.Id, [new SemanticMutationField(new FieldId(2), "attacker")]),
            SemanticMutationBuilder.Update(entity.Id, [new SemanticMutationField(new FieldId(1), "ok")])
        ]);
        var semanticPlan = new SemanticMutationPlanner().Plan(graph);

        // Ordering must not matter: an unauthorized operation cannot be
        // smuggled through by placing it ahead of, or behind, a benign one.
        Assert.Throws<SemanticAuthorizationException>(() =>
            new MutationAuthorizer(schema, policy).Authorize(semanticPlan));
    }

    [Fact]
    public void Batch_authorization_does_not_leak_a_partially_authorized_plan_to_the_caller()
    {
        var entity = Entity(1, "Account", (1, 11), (2, 12));
        var schema = new TestSchema(entity);
        var policy = new AllowOnlyFieldPolicy(new FieldId(1));
        var graph = new SemanticMutationOperationGraph([
            SemanticMutationBuilder.Update(entity.Id, [new SemanticMutationField(new FieldId(1), "ok")]),
            SemanticMutationBuilder.Update(entity.Id, [new SemanticMutationField(new FieldId(2), "attacker")])
        ]);
        var semanticPlan = new SemanticMutationPlanner().Plan(graph);

        SemanticMutationPlan? authorized = null;
        var exception = Record.Exception(() =>
            authorized = new MutationAuthorizer(schema, policy).Authorize(semanticPlan));

        // A caller (e.g. the execution lowering step) must never observe a
        // returned plan when any operation in the batch failed authorization.
        Assert.IsType<SemanticAuthorizationException>(exception);
        Assert.Null(authorized);
    }

    private static MutationEntitySchema Entity(int id, string name, params (int Field, int Column)[] fields)
    {
        var map = fields.ToDictionary(x => new FieldId((ushort)x.Field),
            x => (ColumnId?)new ColumnId((ushort)x.Column));
        var columns = map.Values.Select(x => x!.Value).ToHashSet();
        return new MutationEntitySchema(new EntityId((ushort)id), name, columns, map, columns.First());
    }

    private sealed class AllowOnlyFieldPolicy(FieldId allowed) : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanWriteEntity(EntityId entityId) => true;
        public override bool CanWriteField(EntityId entityId, FieldId fieldId) => fieldId == allowed;
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) => fieldId == allowed;
    }

    private sealed class TestSchema(MutationEntitySchema entity) : IMutationSchema
    {
        public MutationEntitySchema GetEntity(EntityId entityId) =>
            entity.Id == entityId ? entity : throw new KeyNotFoundException();

        public MutationRelationshipSchema GetRelationship(RelationshipId relationshipId) =>
            throw new KeyNotFoundException();
    }
}