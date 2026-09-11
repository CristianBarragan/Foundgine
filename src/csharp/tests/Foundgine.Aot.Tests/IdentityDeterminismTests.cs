using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Foundgine.Core.Abstractions;
using Foundgine.Providers.Aot;
using Foundgine.Providers.Aot.Generator;
using Foundgine.Core.Semantic.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Foundgine.Providers.Aot.Tests;

public sealed class IdentityDeterminismTests
{
    [Fact]
    public void Generated_entity_and_field_ids_are_independent_of_declaration_order()
    {
        const string first = """
                             using Foundgine.Providers.Aot;

                             [FoundgineEntity(StorageName = "customers")]
                             public sealed class Customer
                             {
                                 [FoundgineField(StorageName = "id")]
                                 public int Id { get; init; }

                                 [FoundgineField(StorageName = "name")]
                                 public string Name { get; init; } = "";

                                 [FoundgineField(StorageName = "status")]
                                 public string Status { get; init; } = "";
                             }
                             """;

        const string reordered = """
                                 using Foundgine.Providers.Aot;

                                 [FoundgineEntity(StorageName = "customers")]
                                 public sealed class Customer
                                 {
                                     [FoundgineField(StorageName = "status")]
                                     public string Status { get; init; } = "";

                                     [FoundgineField(StorageName = "name")]
                                     public string Name { get; init; } = "";

                                     [FoundgineField(StorageName = "id")]
                                     public int Id { get; init; }
                                 }
                                 """;

        var firstIds = GenerateIds(first);
        var reorderedIds = GenerateIds(reordered);

        Assert.Equal(
            firstIds.EntityId,
            reorderedIds.EntityId);

        Assert.Equal(
            firstIds.Fields["Id"],
            reorderedIds.Fields["Id"]);

        Assert.Equal(
            firstIds.Fields["Name"],
            reorderedIds.Fields["Name"]);

        Assert.Equal(
            firstIds.Fields["Status"],
            reorderedIds.Fields["Status"]);
    }

    [Fact]
    public void Existing_generated_ids_survive_adding_an_unrelated_module()
    {
        const string customer = """
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

        const string order = """
                             using Foundgine.Providers.Aot;

                             [FoundgineEntity(StorageName = "orders")]
                             public sealed class Order
                             {
                                 [FoundgineField(StorageName = "id")]
                                 public int Id { get; init; }

                                 [FoundgineField(StorageName = "number")]
                                 public string Number { get; init; } = "";
                             }
                             """;

        var independent = GenerateIds(customer);
        var composed = GenerateIds(customer, order);

        Assert.Equal(
            independent.EntityId,
            composed.EntityId);

        Assert.Equal(
            independent.Fields["Id"],
            composed.Fields["Id"]);

        Assert.Equal(
            independent.Fields["Name"],
            composed.Fields["Name"]);
    }

    [Fact]
    public void Generated_ids_are_identical_across_independent_compilations()
    {
        const string source = """
                              using Foundgine.Providers.Aot;

                              [FoundgineEntity(
                                  Name = "Customer",
                                  StorageName = "customers")]
                              public sealed class Customer
                              {
                                  [FoundgineField(StorageName = "id")]
                                  public int Id { get; init; }

                                  [FoundgineField(StorageName = "name")]
                                  public string Name { get; init; } = "";
                              }
                              """;

        var first =
            GenerateIds(
                source,
                assemblyName: "IdentityModuleA");

        var second =
            GenerateIds(
                source,
                assemblyName: "IdentityModuleB");

        Assert.Equal(
            first.EntityId,
            second.EntityId);

        Assert.Equal(
            first.Fields.Keys.OrderBy(x => x),
            second.Fields.Keys.OrderBy(x => x));

        foreach (var fieldName in first.Fields.Keys)
        {
            Assert.Equal(
                first.Fields[fieldName],
                second.Fields[fieldName]);
        }
    }

    [Fact]
    public void Changing_aliases_does_not_change_generated_identity()
    {
        const string withoutAliases = """
                                      using Foundgine.Providers.Aot;

                                      [FoundgineEntity(
                                          Name = "Customer",
                                          StorageName = "customers")]
                                      public sealed class Customer
                                      {
                                          [FoundgineField(
                                              Name = "Name",
                                              StorageName = "name")]
                                          public string Name { get; init; } = "";
                                      }
                                      """;

        const string withAliases = """
                                   using Foundgine.Providers.Aot;

                                   [FoundgineEntity(
                                       Name = "Customer",
                                       StorageName = "customers")]
                                   [FoundgineAlias("Client")]
                                   public sealed class Customer
                                   {
                                       [FoundgineField(
                                           Name = "Name",
                                           StorageName = "name")]
                                       [FoundgineAlias("DisplayName")]
                                       public string Name { get; init; } = "";
                                   }
                                   """;

        var canonical = GenerateIds(withoutAliases);
        var aliased = GenerateIds(withAliases);

        Assert.Equal(
            canonical.EntityId,
            aliased.EntityId);

        Assert.Equal(
            canonical.Fields["Name"],
            aliased.Fields["Name"]);
    }

    [Fact]
    public void All_identity_types_round_trip_through_json()
    {
        var values = new object[]
        {
            new EntityId(ulong.MaxValue),
            new FieldId(4294967297UL),
            new RelationshipId(4294967298UL),
            new ColumnId(4294967299UL),
            new StorageEntityId(4294967300UL),
            new ModelId(4294967301UL),
            new ConnectionId(4294967302UL),
            new AuthorizationId(4294967303UL)
        };

        foreach (var value in values)
        {
            var json =
                System.Text.Json.JsonSerializer.Serialize(
                    value,
                    value.GetType());

            var roundTrip =
                System.Text.Json.JsonSerializer.Deserialize(
                    json,
                    value.GetType());

            Assert.Equal(
                value,
                roundTrip);
        }
    }

    [Theory]
    [InlineData("""
                using Foundgine.Providers.Aot;

                [FoundgineEntity(Id = 0)]
                public sealed class Customer { }
                """)]
    [InlineData("""
                using Foundgine.Providers.Aot;

                [FoundgineEntity]
                public sealed class Customer
                {
                    [FoundgineField(Id = 0)]
                    public int Id { get; init; }
                }
                """)]
    [InlineData("""
                using Foundgine.Providers.Aot;

                [FoundgineEntity]
                public sealed class Customer
                {
                    [FoundgineField]
                    public int Id { get; init; }

                    [FoundgineRelationship(
                        typeof(Order),
                        "CustomerId",
                        "Id",
                        Id = 0)]
                    public Order Orders { get; init; } = null!;
                }

                [FoundgineEntity]
                public sealed class Order
                {
                    [FoundgineField]
                    public int Id { get; init; }

                    [FoundgineField]
                    public int CustomerId { get; init; }
                }
                """)]
    [InlineData("""
                using Foundgine.Providers.Aot;

                [FoundgineEntity]
                public sealed class Customer
                {
                    [FoundgineField(ColumnId = 0)]
                    public int Id { get; init; }
                }
                """)]
    [InlineData("""
                using System;
                using System.Linq.Expressions;
                using Foundgine.Providers.Aot;

                [FoundgineEntity(Id = 1)]
                public sealed class Customer
                {
                    public int Id { get; init; }
                }

                [FoundgineModel(Id = 2)]
                public sealed class CustomerModel
                {
                    public object Customer => null!;
                }

                public static class CustomerAuthorization
                {
                    [FoundgineAuthorization(1, Id = 0)]
                    public static Expression<Func<object, Customer, bool>> CanVisit =>
                        (context, customer) => true;
                }
                """)]
    [InlineData("""
                using Foundgine.Providers.Aot;

                [FoundgineModel(Id = 0)]
                public sealed class CustomerModel { }
                """)]
    public void Explicit_zero_ids_are_rejected_by_the_generator(
        string source)
    {
        var result = RunGenerator(source);

        Assert.NotEmpty(result.Diagnostics);

        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains(
                    "reserved",
                    System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_explicit_entity_ids_are_rejected()
    {
        const string source = """
                              using Foundgine.Providers.Aot;

                              [FoundgineEntity(Id = 77)]
                              public sealed class Customer { }

                              [FoundgineEntity(Id = 77)]
                              public sealed class Account { }
                              """;

        var result = RunGenerator(source);

        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains(
                    "Duplicate Foundgine entity ID",
                    System.StringComparison.Ordinal));
    }

    private static IdentitySnapshot GenerateIds(
        params string[] sources)
        => GenerateIds(
            sources,
            "IdentityRegression");

    private static IdentitySnapshot GenerateIds(
        string source,
        string assemblyName)
        => GenerateIds(
            new[] { source },
            assemblyName);

    private static IdentitySnapshot GenerateIds(
        string[] sources,
        string assemblyName = "IdentityRegression")
    {
        var result =
            RunGenerator(
                sources,
                assemblyName);

        var generated =
            result.OutputCompilation.SyntaxTrees
                .Select(tree =>
                    tree.GetText().ToString())
                .FirstOrDefault(text => text.Contains(
                    "public static class GeneratedMetadata",
                    System.StringComparison.Ordinal));

        Assert.False(
            string.IsNullOrWhiteSpace(generated),
            "The generator did not emit GeneratedMetadata.");

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        var entityMatch = Regex.Match(
            generated!,
            @"new EntityMetadata\(\s*new EntityId\((\d+)\),\s*""Customer""",
            RegexOptions.Singleline);

        Assert.True(
            entityMatch.Success,
            "Customer entity identity was not emitted.");

        var fields =
            Regex.Matches(
                    generated!,
                    @"new FieldMetadata\(new FieldId\((\d+)\),\s*""([^""]+)""",
                    RegexOptions.Singleline)
                .Cast<Match>()
                .ToDictionary(
                    match => match.Groups[2].Value,
                    match => ulong.Parse(
                        match.Groups[1].Value),
                    System.StringComparer.Ordinal);

        return new IdentitySnapshot(
            ulong.Parse(entityMatch.Groups[1].Value),
            fields);
    }

    private static GeneratorRunResultSnapshot RunGenerator(
        string source,
        string assemblyName = "IdentityRegression")
        => RunGenerator(
            new[] { source },
            assemblyName);

    private static GeneratorRunResultSnapshot RunGenerator(
        IEnumerable<string> sources,
        string assemblyName)
    {
        var syntaxTrees =
            sources
                .Select((text, index) =>
                    CSharpSyntaxTree.ParseText(
                        text,
                        new CSharpParseOptions(
                            LanguageVersion.Preview),
                        path: $"Module{index}.cs"))
                .ToArray();

        var references =
            TrustedPlatformReferences()
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(
                        typeof(FoundgineEntityAttribute)
                            .Assembly.Location),

                    MetadataReference.CreateFromFile(
                        typeof(MetadataRegistry)
                            .Assembly.Location),

                    MetadataReference.CreateFromFile(
                        typeof(
                                Foundgine.Core.Abstractions.SemanticIdentity)
                            .Assembly.Location)
                })
                .GroupBy(
                    reference => reference.Display,
                    System.StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

        var compilation =
            CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver =
            CSharpGeneratorDriver.Create(
                new ISourceGenerator[]
                {
                    new FoundgineMetadataGenerator()
                        .AsSourceGenerator()
                },
                parseOptions:
                new CSharpParseOptions(
                    LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);

        return new GeneratorRunResultSnapshot(
            outputCompilation,
            diagnostics
                .Concat(outputCompilation.GetDiagnostics())
                .ToArray());
    }

    private static IEnumerable<MetadataReference>
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

    private sealed record IdentitySnapshot(
        ulong EntityId,
        Dictionary<string, ulong> Fields);

    private sealed record GeneratorRunResultSnapshot(
        Compilation OutputCompilation,
        IReadOnlyList<Diagnostic> Diagnostics);
}