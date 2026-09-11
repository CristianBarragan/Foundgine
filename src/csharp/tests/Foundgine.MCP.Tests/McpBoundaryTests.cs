using System.Reflection;
using Foundgine.Providers.Tools.MCP;
using ModelContextProtocol.Server;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpBoundaryTests
{
    [Fact]
    public void Tool_surface_is_transport_only()
    {
        var type = typeof(FoundgineMcpTools);

        Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());
        Assert.NotNull(type.GetMethod(nameof(FoundgineMcpTools.DescribeCapabilities))
            ?.GetCustomAttribute<McpServerToolAttribute>());
        Assert.NotNull(type.GetMethod(nameof(FoundgineMcpTools.ExecuteQueryAsync))
            ?.GetCustomAttribute<McpServerToolAttribute>());
    }

    [Fact]
    public void Adapter_project_does_not_reference_provider_projects()
    {
        var references = typeof(FoundgineMcpTools).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .Where(x => x is not null)
            .ToArray();

        Assert.DoesNotContain(references,
            x => x!.Contains("Foundgine.Providers.Storage.Sql", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x!.Contains("HotChocolate", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x!.Contains("EntityFramework", StringComparison.Ordinal));
    }
}

// M5 contract: MCP security identity is host-supplied, never taken from tool JSON.