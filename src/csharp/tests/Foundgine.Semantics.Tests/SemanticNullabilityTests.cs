using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticNullabilityTests
{
    [Fact]
    public void Typed_builder_preserves_nullable_reference_annotation()
    {
        var model = new SemanticModelBuilder()
            .Entity<NullableProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Build();

        var field = model.ResolveEntity("Product").Fields.Single(x => x.Name == "Name");

        Assert.True(field.IsNullable);
    }

    [Fact]
    public void Typed_builder_preserves_non_nullable_reference_annotation()
    {
        var model = new SemanticModelBuilder()
            .Entity<NonNullableProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Build();

        var field = model.ResolveEntity("Product").Fields.Single(x => x.Name == "Name");

        Assert.False(field.IsNullable);
    }

    [Fact]
    public void Nullability_is_part_of_the_contract_fingerprint()
    {
        var nullable = new SemanticModelBuilder()
            .Entity<NullableProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Build();
        var nonNullable = new SemanticModelBuilder()
            .Entity<NonNullableProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Build();

        Assert.NotEqual(nullable.ContractFingerprint, nonNullable.ContractFingerprint);
    }

    private sealed class NullableProduct
    {
        public int Id { get; init; }
        public string? Name { get; init; }
    }

    private sealed class NonNullableProduct
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
    }
}