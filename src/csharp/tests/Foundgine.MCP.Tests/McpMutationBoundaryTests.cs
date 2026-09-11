using System.Reflection;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class McpMutationBoundaryTests
{
    [Fact]
    public void MutationToolsAreTransportOnly()
    {
        var type = typeof(FoundgineMcpMutationTools);

        var surfaceTypes = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .Concat(type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                            BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.FieldType))
            .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(p => p.PropertyType))
            .Select(t => t.FullName ?? t.Name)
            .Distinct()
            .ToArray();

        var offenders = surfaceTypes
            .Where(name =>
                name.Contains("Foundgine.Providers.Storage.Sql", StringComparison.Ordinal) ||
                name.Contains("Npgsql", StringComparison.Ordinal) ||
                name.Contains("HotChocolate", StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "FoundgineMcpMutationTools must stay transport-only (no SQL/Npgsql/HotChocolate types " +
            "in its own member signatures), even though SQL storage now shares its assembly under " +
            "the v2 package restructuring. Offenders: " + string.Join(", ", offenders));
    }
}