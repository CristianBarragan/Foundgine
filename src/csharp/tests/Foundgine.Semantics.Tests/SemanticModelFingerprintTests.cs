using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticModelFingerprintTests
{
    [Fact]
    public void Fingerprint_is_stable_when_declarations_are_reordered()
    {
        var first = BuildModel(reordered: false);
        var second = BuildModel(reordered: true);

        Assert.Equal(first.ContractFingerprint, second.ContractFingerprint);
    }

    [Fact]
    public void Fingerprint_changes_when_semantic_contract_changes()
    {
        var first = BuildModel(reordered: false);
        var changed = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .Field(x => x.Price,
                    capabilities: SemanticFieldCapabilities.Default | SemanticFieldCapabilities.Writable))
            .Build();

        Assert.NotEqual(first.ContractFingerprint, changed.ContractFingerprint);
    }

    [Fact]
    public void Fingerprint_does_not_depend_on_clr_model_type_identity()
    {
        var typed = BuildModel(reordered: false);
        var untyped = new SemanticModelBuilder()
            .Entity(EntityId.Create("Product"), "Product", e => e
                .Identity(FieldId.Create("Product", "Id"), "Id")
                .Field(FieldId.Create("Product", "Name"), "Name", typeof(string))
                .Field(FieldId.Create("Product", "Price"), "Price", typeof(decimal)))
            .Build();

        Assert.Equal(typed.ContractFingerprint, untyped.ContractFingerprint);
    }


    [Fact]
    public void Semantic_model_version_is_the_contract_fingerprint()
    {
        var model = BuildModel(false);

        var version = SemanticVersionSet.For(model);

        Assert.Equal($"sha256:{model.ContractFingerprint}", version.SemanticModelVersion);
    }

    [Fact]
    public void Fingerprint_changes_when_alias_changes()
    {
        var first = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "DisplayName"))
            .Build();
        var changed = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "ProductName"))
            .Build();

        Assert.NotEqual(first.ContractFingerprint, changed.ContractFingerprint);
        Assert.NotEqual(SemanticVersionSet.For(first).SemanticModelVersion,
            SemanticVersionSet.For(changed).SemanticModelVersion);
    }

    [Fact]
    public void Fingerprint_changes_when_only_the_declared_alias_weight_changes()
    {
        // Alias weight is security-relevant evidence, not decorative metadata.
        // A contract that only relaxes/tightens a declared alias weight is a
        // different contract and must not silently share a fingerprint with
        // the original — otherwise an audit trail keyed on ContractFingerprint
        // could not distinguish "Vendor is weight-90 evidence for Supplier"
        // from "Vendor is weight-40 evidence for Supplier".
        var first = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "DisplayName", 90))
            .Build();
        var changed = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "DisplayName", 40))
            .Build();

        Assert.NotEqual(first.ContractFingerprint, changed.ContractFingerprint);
    }

    [Fact]
    public void Fingerprint_is_unaffected_by_unweighted_vs_weighted_alias_of_the_same_name()
    {
        // An alias with no declared weight is a different declaration from
        // the same alias name with a weight, so the fingerprint must still
        // distinguish them even though only the weight component differs.
        var unweighted = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "DisplayName"))
            .Build();
        var weighted = new SemanticModelBuilder()
            .Entity<TestProduct>(EntityId.Create("Product"), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "DisplayName", 50))
            .Build();

        Assert.NotEqual(unweighted.ContractFingerprint, weighted.ContractFingerprint);
    }

    [Fact]
    public void Fingerprint_is_lowercase_sha256()
    {
        var fingerprint = BuildModel(false).ContractFingerprint;

        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }

    private static SemanticModel BuildModel(bool reordered) => new SemanticModelBuilder()
        .Entity<TestProduct>(EntityId.Create("Product"), "Product", e =>
        {
            e.Identity(x => x.Id);
            if (reordered)
            {
                e.Field(x => x.Price);
                e.Field(x => x.Name);
            }
            else
            {
                e.Field(x => x.Name);
                e.Field(x => x.Price);
            }
        })
        .Build();

    private sealed class TestProduct
    {
        public int Id { get; init; }
        public string? Name { get; init; }
        public decimal Price { get; init; }
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