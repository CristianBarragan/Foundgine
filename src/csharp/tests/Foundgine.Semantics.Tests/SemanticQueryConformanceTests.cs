using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Semantics.Tests;

/// <summary>
/// Cross-language conformance for <see cref="SemanticQuery"/>: asserts the vectors in
/// src/conformance/semantic-query-vectors.json, the same file the Java <c>SemanticQueryConformanceTest</c> asserts.
/// The builders must produce the same operation (rendered canonically by names) or the same error message.
/// Set FOUNDGINE_CONFORMANCE_DIR to override the vector directory.
/// </summary>
public sealed class SemanticQueryConformanceTests
{
    public static IEnumerable<object[]> Names()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Builder_matches_the_reference_vector(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(VectorFile()));
        var model = BuildModel(doc.RootElement.GetProperty("model"));
        var vector = doc.RootElement.GetProperty("cases").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == name);

        if (vector.TryGetProperty("error", out var error))
        {
            var ex = Assert.ThrowsAny<ArgumentException>(() => Run(model, vector));
            Assert.Equal(error.GetString(), ex.Message);
            return;
        }

        Assert.Equal(vector.GetProperty("rendered").GetString(), Render(model, Run(model, vector).Root));
    }

    private static SemanticOperation Run(MetadataRegistry model, JsonElement vector)
    {
        var query = SemanticQuery.From(model, vector.GetProperty("entity").GetString()!);
        Apply(model, query, vector.GetProperty("steps"));
        return query.Build();
    }

    private static void Apply(MetadataRegistry model, SemanticQuery query, JsonElement steps)
    {
        foreach (var step in steps.EnumerateArray())
        {
            switch (step.GetProperty("op").GetString())
            {
                case "select":
                    query.Select(step.GetProperty("names").EnumerateArray().Select(x => x.GetString()!).ToArray());
                    break;
                case "whereEq": query.WhereEq(Field(step), Value(step.GetProperty("value"))); break;
                case "whereNeq": query.WhereNeq(Field(step), Value(step.GetProperty("value"))); break;
                case "whereIn":
                    query.WhereIn(Field(step), step.GetProperty("values").EnumerateArray().Select(Value).ToArray());
                    break;
                case "orderBy":
                    query.OrderBy(Field(step), step.GetProperty("dir").GetString() == "desc"
                        ? SemanticSortDirection.Desc
                        : SemanticSortDirection.Asc);
                    break;
                case "take": query.Take(step.GetProperty("n").GetInt32()); break;
                case "skip": query.Skip(step.GetProperty("n").GetInt32()); break;
                case "include":
                    var relName = step.GetProperty("relationship").GetString();
                    var rel = model.Relationships.Single(r => r.Name == relName);
                    query.Include(rel.Id, child => Apply(model, child, step.GetProperty("steps")));
                    break;
                default: throw new InvalidOperationException("Unknown step " + step);
            }
        }
    }

    private static string Field(JsonElement step) => step.GetProperty("field").GetString()!;

    private static object? Value(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetInt32();

    // ---- canonical rendering (spec semantic-query-v1, see conformance/README.md) ----

    private static string Render(MetadataRegistry model, SemanticReadNode node)
    {
        var entity = model.Get(node.EntityId);
        var names = model.Entities.SelectMany(e => e.EffectiveFields).ToDictionary(f => f.Id, f => f.Name);
        var sb = new System.Text.StringBuilder();
        sb.Append($"#{node.Id} {entity.Name}({string.Join(", ", node.Fields.Select(f => names[f]))})");
        if (node.ViaRelationship is { } via) sb.Append(" via ").Append(model.Get(via).Name);
        var options = node.QueryOptions;
        if (options?.Filter is { } filter) sb.Append(" where ").Append(Filter(filter, names));
        if (options is { Order.Count: > 0 })
            sb.Append(" order ").Append(string.Join(", ", options.Order!.Select(o =>
                $"{names[o.Field]} {(o.Direction == SemanticSortDirection.Desc ? "desc" : "asc")}")));
        if (options?.Limit is { } limit) sb.Append(" limit ").Append(limit);
        if (options?.Offset is { } offset) sb.Append(" offset ").Append(offset);
        if (node.Children.Count > 0)
            sb.Append(" [").Append(string.Join("; ", node.Children.Select(c => Render(model, c)))).Append(']');
        return sb.ToString();
    }

    private static string Filter(SemanticFilterExpression filter, Dictionary<FieldId, string> names) => filter switch
    {
        SemanticAndFilter and => "(" + string.Join(" AND ", and.Expressions.Select(e => Filter(e, names))) + ")",
        SemanticFieldFilter f => f.Operator switch
        {
            SemanticFilterOperator.Eq => $"{names[f.Field]} eq {Scalar(f.Value)}",
            SemanticFilterOperator.Neq => $"{names[f.Field]} neq {Scalar(f.Value)}",
            SemanticFilterOperator.In =>
                $"{names[f.Field]} in [{string.Join(", ", ((IEnumerable<object?>)f.Value!).Select(Scalar))}]",
            _ => throw new InvalidOperationException(f.Operator.ToString())
        },
        _ => throw new InvalidOperationException(filter.GetType().Name)
    };

    private static string Scalar(object? v) => v is string s ? JsonSerializer.Serialize(s) : Convert.ToString(v)!;

    private static MetadataRegistry BuildModel(JsonElement model)
    {
        var registry = new MetadataRegistry();
        foreach (var entity in model.GetProperty("entities").EnumerateObject())
        {
            var fields = entity.Value.EnumerateArray()
                .Select(f => new FieldMetadata(FieldId.Create(entity.Name, f[0].GetString()!), f[0].GetString()!,
                    f[1].GetString() == "int" ? typeof(int) : typeof(string)))
                .ToList();
            registry.Register(new EntityMetadata(EntityId.Create(entity.Name), entity.Name, [], Fields: fields));
        }

        foreach (var r in model.GetProperty("relationships").EnumerateArray())
        {
            var (source, target, name) = (r.GetProperty("source").GetString()!, r.GetProperty("target").GetString()!,
                r.GetProperty("name").GetString()!);
            registry.Register(new RelationshipMetadata(RelationshipId.Create(source, name),
                EntityId.Create(source), EntityId.Create(target), name,
                new ColumnReference(EntityId.Create(source), ColumnId.Create(source, "Id")),
                new ColumnReference(EntityId.Create(target), ColumnId.Create(target, source + "Id"))));
        }

        return registry;
    }

    private static string VectorFile()
    {
        var overrideDir = Environment.GetEnvironmentVariable("FOUNDGINE_CONFORMANCE_DIR");
        if (overrideDir is not null) return Path.Combine(overrideDir, "semantic-query-vectors.json");
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "conformance", "semantic-query-vectors.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Conformance vectors not found; set FOUNDGINE_CONFORMANCE_DIR.");
    }
}
