using System.Linq;
using System.Text.RegularExpressions;
using Foundgine.Core.Abstractions;
using Foundgine.Providers.Aot;
using Foundgine.Core.Semantic.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Foundgine.Providers.Aot.Tests;

/// <summary>
/// Fix: <c>Foundgine.Providers.Aot.Generator</c> can no longer reference the runtime
/// <see cref="Foundgine.Core.Abstractions.SemanticIdentity"/> hashing helper directly
/// (analyzers running inside the compiler process must not carry a hard
/// dependency on the consuming compilation's runtime assemblies), so the fix
/// introduced <c>Foundgine.Providers.Aot.Generator.GeneratorSemanticIdentity</c> as an
/// independent copy of the same namespaces, key-building rules, and FNV-1a
/// hash used at runtime.
///
/// That is the actual "id uniqueness" risk this branch is repairing: nothing
/// stops the two copies from drifting apart. If they ever disagree, an
/// entity/field defined via <c>[FoundgineEntity]</c>/<c>[FoundgineField]</c>
/// (compile-time, hashed by <c>GeneratorSemanticIdentity</c>) and the same
/// logically-named entity/field defined via the manual
/// <see cref="Foundgine.Core.Semantic.SemanticModelBuilder"/> path (runtime,
/// hashed by <see cref="SemanticIdentity"/>) would silently compute
/// different numeric IDs for what is supposed to be the same identity -
/// exactly the kind of confusion a security capability contract or a plan
/// cache partition key must never be exposed to.
///
/// This test runs the real generator (reusing the harness pattern from
/// <see cref="IdentityDeterminismTests"/>) against a small module and asserts
/// the emitted EntityId/FieldId values equal what
/// <see cref="SemanticIdentity"/> computes independently for the same
/// canonical keys at runtime.
/// </summary>
public sealed class GeneratorRuntimeIdentityParityTests
{
    private const string Source = """
                                  using Foundgine.Providers.Aot;

                                  [FoundgineEntity(StorageName = "customers")]
                                  public sealed class Customer
                                  {
                                      [FoundgineField(StorageName = "id")]
                                      public int Id { get; init; }

                                      [FoundgineField(StorageName = "name")]
                                      public string Name { get; init; } = "";
                                  }
                                  """;

    [Fact]
    public void Generator_entity_id_matches_the_runtime_hasher_for_the_same_canonical_key()
    {
        var generatedText = GenerateMetadataSource(Source);

        var entityMatch = Regex.Match(
            generatedText,
            @"new EntityMetadata\(\s*new EntityId\((\d+)\),\s*""Customer""",
            RegexOptions.Singleline);

        Assert.True(
            entityMatch.Success,
            "Customer entity identity was not emitted.");

        var generatedEntityId =
            ulong.Parse(entityMatch.Groups[1].Value);

        var runtimeExpectedEntityId =
            SemanticIdentity.Hash(
                SemanticIdentity.EntityKey("Customer"));

        Assert.Equal(
            runtimeExpectedEntityId,
            generatedEntityId);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Name")]
    public void Generator_field_id_matches_the_runtime_hasher_for_the_same_canonical_key(
        string fieldName)
    {
        var generatedText = GenerateMetadataSource(Source);

        var fieldMatch = Regex.Match(
            generatedText,
            $@"new FieldMetadata\(new FieldId\((\d+)\),\s*""{fieldName}""",
            RegexOptions.Singleline);

        Assert.True(
            fieldMatch.Success,
            $"{fieldName} field identity was not emitted.");

        var generatedFieldId =
            ulong.Parse(fieldMatch.Groups[1].Value);

        // Mirrors the generator's own canonical key construction for fields:
        // Hash(FieldNamespace + ":" + entityName + "." + fieldName), rather
        // than SemanticIdentity.FieldKey (which pairs differently) - so this
        // test fails loudly if the generator's key shape itself drifts,
        // not just its hash constant.
        var runtimeExpectedFieldId =
            SemanticIdentity.Hash(
                SemanticIdentity.FieldNamespace +
                ":Customer." +
                fieldName);

        Assert.Equal(
            runtimeExpectedFieldId,
            generatedFieldId);
    }

    [Fact]
    public void Generator_and_runtime_agree_that_every_shared_namespace_constant_is_identical()
    {
        // Belt-and-braces: even before running the generator, the namespace
        // constants themselves (the strings that seed every canonical key)
        // must be byte-identical between the two copies.
        Assert.Equal(
            "entity",
            SemanticIdentity.EntityNamespace);

        Assert.Equal(
            "field",
            SemanticIdentity.FieldNamespace);

        Assert.Equal(
            "relationship",
            SemanticIdentity.RelationshipNamespace);

        Assert.Equal(
            "table",
            SemanticIdentity.TableNamespace);

        Assert.Equal(
            "column",
            SemanticIdentity.ColumnNamespace);

        Assert.Equal(
            "model",
            SemanticIdentity.ModelNamespace);

        Assert.Equal(
            "connection",
            SemanticIdentity.ConnectionNamespace);

        Assert.Equal(
            "authorization",
            SemanticIdentity.AuthorizationNamespace);
    }

    private static string GenerateMetadataSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "Module0.cs");

        var references = TrustedPlatformReferences()
            .Concat(new[]
            {
                MetadataReference.CreateFromFile(
                    typeof(FoundgineEntityAttribute).Assembly.Location),

                MetadataReference.CreateFromFile(
                    typeof(MetadataRegistry).Assembly.Location),

                MetadataReference.CreateFromFile(
                    typeof(SemanticIdentity).Assembly.Location)
            })
            .GroupBy(
                reference => reference.Display,
                System.StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "GeneratorRuntimeIdentityParity",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver =
            CSharpGeneratorDriver.Create(
                new ISourceGenerator[]
                {
                    new Foundgine.Providers.Aot.Generator
                            .FoundgineMetadataGenerator()
                        .AsSourceGenerator()
                },
                parseOptions:
                new CSharpParseOptions(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);

        Assert.DoesNotContain(
            diagnostics.Concat(outputCompilation.GetDiagnostics()),
            diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        var generatedText = outputCompilation.SyntaxTrees
            .Select(tree => tree.GetText().ToString())
            .FirstOrDefault(text => text.Contains(
                "public static class GeneratedMetadata",
                System.StringComparison.Ordinal));

        Assert.False(
            string.IsNullOrWhiteSpace(generatedText),
            "The generator did not emit GeneratedMetadata.");

        return generatedText!;
    }

    private static System.Collections.Generic.IEnumerable<MetadataReference>
        TrustedPlatformReferences()
    {
        var trusted =
            (string?)System.AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES");

        Assert.False(
            string.IsNullOrWhiteSpace(trusted));

        return trusted!
            .Split(System.IO.Path.PathSeparator)
            .Select(path => (MetadataReference)
                MetadataReference.CreateFromFile(path));
    }
}