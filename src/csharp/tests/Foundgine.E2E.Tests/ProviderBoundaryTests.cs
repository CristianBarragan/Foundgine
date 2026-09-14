using System.Reflection;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class ProviderBoundaryTests
{
    [Fact]
    public void Provider_mutation_plan_is_opaque_to_planning_types()
    {
        var type = typeof(ProviderMutationPlan);

        Assert.Empty(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Provider_mutation_batch_plan_exposes_only_provider_plans()
    {
        var property = typeof(ProviderMutationBatchPlan)
            .GetProperty(nameof(ProviderMutationBatchPlan.Operations));

        Assert.NotNull(property);
        Assert.Equal(typeof(IReadOnlyList<ProviderMutationPlan>), property!.PropertyType);
    }

    [Fact]
    public void Execution_does_not_reference_sql_provider()
    {
        var references = typeof(ExecutionResult).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Foundgine.Providers.Storage.Sql", references);
    }
}
