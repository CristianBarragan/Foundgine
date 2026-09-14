using Foundgine.Core.Abstractions;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class AdapterBoundaryTests
{
    [Fact]
    public void Query_adapter_does_not_reference_planning_execution_or_sql()
    {
        var references = typeof(HotChocolateSemanticAdapter).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .Where(x => x is not null)
            .ToArray();

        Assert.DoesNotContain("Foundgine.Core.Semantic.Planning", references);
        Assert.DoesNotContain("Foundgine.Core.Execution", references);
        Assert.DoesNotContain("Foundgine.Providers.Storage.Sql", references);
    }
}
