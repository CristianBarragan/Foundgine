using System.Xml.Linq;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Core_projects_do_not_reference_transport_or_provider_packages()
    {
        // v2 package restructuring (see PackageReleaseNotes in each csproj) consolidated the
        // former per-module packages (Foundgine.Core.Abstractions, Foundgine.Core.Semantic,
        // Foundgine.Core.Semantic.Planning, Foundgine.Core.Execution, ...) into two packages:
        // Foundgine.Core (provider-independent semantic layer) and Foundgine.Runtime (execution
        // layer, which is allowed to depend on Foundgine.Core but nothing transport/provider).
        var root = FindRepositoryRoot();

        AssertProjectReferencesDoNotContain(
            root,
            "src/csharp/Foundgine.Core/Foundgine.Core.csproj",
            "GraphQL", "HotChocolate", "Sql", "Npgsql", "InMemory", "Intent.Json", "Aot");

        AssertProjectReferencesDoNotContain(
            root,
            "src/csharp/Foundgine.Runtime/Foundgine.Runtime.csproj",
            "GraphQL", "HotChocolate", "Sql", "Npgsql", "InMemory");
    }

    [Fact]
    public void Active_source_contains_no_graphgine_references()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "src");

        var offenders = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("Graphgine", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Active src/ must not depend on historical Graphgine material. Offenders: " +
            string.Join(", ", offenders));
    }

    private static void AssertProjectReferencesDoNotContain(string root, string relativeProject,
        params string[] forbidden)
    {
        var path = Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(path);

        var references = document.Descendants("ProjectReference")
            .Select(x => (string?)x.Attribute("Include") ?? string.Empty)
            .Concat(document.Descendants("PackageReference")
                .Select(x => (string?)x.Attribute("Include") ?? string.Empty))
            .ToArray();

        var offenders = references
            .Where(reference => forbidden.Any(term =>
                reference.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"{relativeProject} has forbidden dependencies: {string.Join(", ", offenders)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Foundgine.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Foundgine repository root.");
    }
}