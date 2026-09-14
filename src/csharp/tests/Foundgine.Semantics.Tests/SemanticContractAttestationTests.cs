using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticContractAttestationTests
{
    [Fact]
    public void Matching_fingerprint_is_accepted()
    {
        var model = BuildModel();

        Assert.True(SemanticContractAttestation.Matches(model, model.ContractFingerprint));
        Assert.True(SemanticContractAttestation.Matches(model, $"sha256:{model.ContractFingerprint}"));
    }

    [Fact]
    public void Mismatched_fingerprint_is_rejected()
    {
        var model = BuildModel();

        Assert.False(SemanticContractAttestation.Matches(model, new string('0', 64)));
        var error = Assert.Throws<InvalidOperationException>(() =>
            SemanticContractAttestation.EnsureMatches(model, new string('0', 64)));

        Assert.Contains(model.ContractFingerprint, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attestation_is_independent_of_semantic_version_prefix()
    {
        var model = BuildModel();
        var version = SemanticVersionSet.For(model);

        Assert.True(SemanticContractAttestation.Matches(model, version.SemanticModelVersion));
    }

    private static SemanticModel BuildModel() => new SemanticModelBuilder()
        .Entity<TestCustomer>(EntityId.Create("Customer"), "Customer", e => e
            .Identity(x => x.Id)
            .Field(x => x.Name))
        .Build();

    private sealed class TestCustomer
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
    }
}
