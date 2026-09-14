using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Mutation;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticMutationIrTests
{
    [Fact]
    public void CreateUsesSemanticFieldIdentityAndProducesSemanticEffects()
    {
        var entity = new EntityId(10);
        var name = new FieldId(20);

        var operation = SemanticMutationBuilder.Create(
            entity,
            [new SemanticMutationField(name, "Alice")]);

        Assert.Equal(SemanticMutationKind.Create, operation.Kind);
        Assert.Equal(name, operation.Fields[0].Field);
        Assert.All(operation.Effects, e => Assert.NotEqual(default(EntityId), e.Entity));
        Assert.Contains(operation.Effects, e =>
            e.Kind == SemanticMutationEffectKind.CreateEntity && e.Entity == entity);
        Assert.Contains(operation.Effects, e =>
            e.Kind == SemanticMutationEffectKind.SetField && e.Field == name);
    }

    [Fact]
    public void UpsertConflictIsSemanticFieldIdentity()
    {
        var operation = SemanticMutationBuilder.Upsert(
            new EntityId(10),
            [new SemanticMutationField(new FieldId(20), "A")],
            [new FieldId(21)]);

        Assert.Equal(SemanticMutationKind.Upsert, operation.Kind);
        Assert.Equal(new FieldId(21), Assert.Single(operation.ConflictFields));
        Assert.Contains(operation.Effects, e => e.Kind == SemanticMutationEffectKind.UpsertEntity);
    }

    [Fact]
    public void DependencyUsesSemanticFieldsAndOptionalRelationship()
    {
        var dependency = new SemanticMutationDependency(
            0,
            1,
            new FieldId(20),
            new FieldId(30),
            new RelationshipId(40));

        Assert.Equal(0, dependency.SourceOperationIndex);
        Assert.Equal(new FieldId(20), dependency.SourceField);
        Assert.Equal(new FieldId(30), dependency.TargetField);
        Assert.Equal(new RelationshipId(40), dependency.Relationship);
    }

    [Fact]
    public void MutationGraphAggregatesEffectsWithoutProviderConcepts()
    {
        var op = SemanticMutationBuilder.Create(
            new EntityId(10),
            [new SemanticMutationField(new FieldId(20), "A")]);

        var graph = new SemanticMutationOperationGraph([op]);

        Assert.Equal(2, graph.Effects.Count());
        Assert.All(graph.Effects, effect => Assert.NotEqual(default, effect.Entity));
    }
}
