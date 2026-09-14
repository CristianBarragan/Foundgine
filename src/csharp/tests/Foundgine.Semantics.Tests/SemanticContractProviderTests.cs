using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticContractProviderTests
{
    [Fact]
    public void Provider_exposes_the_same_immutable_snapshot()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", entity => entity.Identity(new FieldId(1), "Id"))
            .Build()
            .Freeze();

        var snapshot = model.CreateSnapshot();
        var provider = new SemanticContractProvider(snapshot);

        Assert.Same(snapshot, provider.Contract);
        Assert.Equal(model.ContractFingerprint, provider.Contract.ContractFingerprint);
    }

    [Fact]
    public void Provider_does_not_expose_model_or_builder_lifecycle()
    {
        var members = typeof(ISemanticContractProvider)
            .GetProperties()
            .Select(x => x.PropertyType)
            .ToArray();

        Assert.Contains(typeof(SemanticContractSnapshot), members);
        Assert.DoesNotContain(typeof(SemanticModel), members);
        Assert.DoesNotContain(typeof(SemanticModelBuilder), members);
    }
}