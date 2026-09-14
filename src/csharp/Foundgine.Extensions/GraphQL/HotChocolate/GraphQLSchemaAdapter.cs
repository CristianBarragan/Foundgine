using Foundgine.Core.Semantic;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Builds the GraphQL schema vocabulary from Foundgine's semantic model.
/// Introspection itself remains a responsibility of the GraphQL host; this
/// adapter supplies the schema that the host exposes and therefore keeps
/// __schema/__type outside the Foundgine core.
/// </summary>
public sealed class GraphQLSchemaAdapter
{
    private readonly SemanticModel _model;

    public GraphQLSchemaAdapter(SemanticModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public GraphQLSchemaDescriptor Build()
    {
        var objects = _model.Entities
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildObject)
            .ToArray();

        var inputs = _model.Entities
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => new[] { BuildInput(x), BuildWhereInput(x) })
            .ToArray();

        var mutations = _model.Entities
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(BuildMutations)
            .ToArray();

        return new GraphQLSchemaDescriptor(
            [new GraphQLObjectTypeDescriptor("Query", BuildQueryFields(objects).ToArray())],
            [new GraphQLObjectTypeDescriptor("Mutation", mutations)],
            inputs);
    }

    /// <summary>
    /// Produces SDL suitable for registration with a GraphQL host. A host such
    /// as Hot Chocolate can then expose standard GraphQL introspection over the
    /// resulting schema.
    /// </summary>
    public string BuildSdl()
    {
        var schema = Build();
        var lines = new List<string>
        {
            "type Query {"
        };
        foreach (var field in schema.QueryTypes[0].Fields)
            lines.Add(FormatField(field));
        lines.Add("}");

        lines.Add("");
        lines.Add("type Mutation {");
        foreach (var field in schema.MutationTypes[0].Fields)
            lines.Add(FormatField(field));
        lines.Add("}");

        foreach (var entity in _model.Entities.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("");
            lines.Add($"type {entity.Name} {{");
            lines.Add("  id: ID!");
            foreach (var field in entity.Fields.Where(f => f.Id != entity.Identity.FieldId))
                lines.Add($"  {ToGraphQLName(field.Name)}: {GraphQLTypeMapper.Map(field.ClrType)}");
            foreach (var relationship in entity.Relationships)
            {
                var target = _model.Get(relationship.Target);
                var suffix = relationship.Cardinality == RelationshipCardinality.Many ? "!" : "!";
                var type = relationship.Cardinality == RelationshipCardinality.Many
                    ? $"[{target.Name}!]!"
                    : $"{target.Name}{suffix}";
                lines.Add($"  {ToGraphQLName(relationship.Name)}: {type}");
            }

            lines.Add("}");
        }

        foreach (var input in schema.InputTypes)
        {
            lines.Add("");
            lines.Add($"input {input.Name} {{");
            foreach (var field in input.Fields)
                lines.Add($"  {field.Name}: {FormatType(field)}");
            lines.Add("}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private GraphQLObjectTypeDescriptor BuildObject(SemanticEntity entity)
    {
        var fields = new List<GraphQLFieldDescriptor>
        {
            new("id", "ID", IsNonNull: true)
        };
        fields.AddRange(entity.Fields
            .Where(x => x.Id != entity.Identity.FieldId)
            .Select(x => new GraphQLFieldDescriptor(ToGraphQLName(x.Name), GraphQLTypeMapper.Map(x.ClrType))));
        fields.AddRange(entity.Relationships.Select(r =>
        {
            var target = _model.Get(r.Target);
            return r.Cardinality == RelationshipCardinality.Many
                ? new GraphQLFieldDescriptor(ToGraphQLName(r.Name), target.Name, IsList: true, IsNonNull: true)
                : new GraphQLFieldDescriptor(ToGraphQLName(r.Name), target.Name, IsNonNull: true);
        }));
        return new GraphQLObjectTypeDescriptor(entity.Name, fields);
    }

    private GraphQLInputTypeDescriptor BuildInput(SemanticEntity entity)
    {
        var fields = entity.Fields
            .Where(x => x.Id != entity.Identity.FieldId)
            .Select(x => new GraphQLInputFieldDescriptor(ToGraphQLName(x.Name), GraphQLTypeMapper.Map(x.ClrType)))
            .ToArray();
        return new GraphQLInputTypeDescriptor($"{entity.Name}Input", fields);
    }

    private GraphQLInputTypeDescriptor BuildWhereInput(SemanticEntity entity)
    {
        var fields = new List<GraphQLInputFieldDescriptor>
        {
            new("id", "Int")
        };

        fields.AddRange(entity.Fields
            .Where(x => x.Id != entity.Identity.FieldId)
            .Select(x => new GraphQLInputFieldDescriptor(
                ToGraphQLName(x.Name),
                GraphQLTypeMapper.Map(x.ClrType))));

        return new GraphQLInputTypeDescriptor($"{entity.Name}WhereInput", fields);
    }

    private IEnumerable<GraphQLFieldDescriptor> BuildMutations(SemanticEntity entity)
    {
        var input = new[] { new GraphQLArgumentDescriptor("input", $"{entity.Name}Input", IsNonNull: true) };
        var where = new[] { new GraphQLArgumentDescriptor("where", $"{entity.Name}WhereInput", IsNonNull: true) };
        yield return new GraphQLFieldDescriptor($"create{entity.Name}", entity.Name, IsNonNull: true, Arguments: input);
        yield return new GraphQLFieldDescriptor($"update{entity.Name}", entity.Name, IsNonNull: true,
            Arguments: [input[0], where[0]]);
        yield return new GraphQLFieldDescriptor($"delete{entity.Name}", entity.Name, IsNonNull: true, Arguments: where);
        yield return new GraphQLFieldDescriptor(
            $"upsert{entity.Name}",
            entity.Name,
            IsNonNull: true,
            Arguments:
            [
                input[0],
                new GraphQLArgumentDescriptor("onConflict", "String", IsList: true)
            ]);
    }

    private static IEnumerable<GraphQLFieldDescriptor> BuildQueryFields(
        IEnumerable<GraphQLObjectTypeDescriptor> objects)
    {
        foreach (var type in objects)
            yield return new GraphQLFieldDescriptor(ToGraphQLName(type.Name), type.Name, IsNonNull: true);
    }

    private static string FormatField(GraphQLFieldDescriptor field)
    {
        var args = field.Arguments is { Count: > 0 }
            ? "(" + string.Join(", ", field.Arguments.Select(a => $"{a.Name}: {FormatType(a)}")) + ")"
            : string.Empty;
        return $"  {field.Name}{args}: {FormatType(field)}";
    }

    private static string FormatType(GraphQLFieldDescriptor field) =>
        FormatType(field.Type, field.IsList, field.IsNonNull);

    private static string FormatType(GraphQLInputFieldDescriptor field) =>
        FormatType(field.Type, field.IsList, field.IsNonNull);

    private static string FormatType(GraphQLArgumentDescriptor field) =>
        FormatType(field.Type, field.IsList, field.IsNonNull);

    private static string FormatType(string type, bool isList, bool isNonNull)
    {
        var result = isList ? $"[{type}!]" : type;
        return isNonNull ? result + "!" : result;
    }

    private static string ToGraphQLName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

internal static class GraphQLTypeMapper
{
    public static string Map(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        var effective = nullable ?? type;
        if (effective == typeof(string) || effective == typeof(char) || effective == typeof(Guid)) return "String";
        if (effective == typeof(bool)) return "Boolean";
        if (effective == typeof(byte) || effective == typeof(short) || effective == typeof(int) ||
            effective == typeof(long)) return "Int";
        if (effective == typeof(float) || effective == typeof(double) || effective == typeof(decimal)) return "Float";
        if (effective == typeof(DateTime) || effective == typeof(DateTimeOffset)) return "DateTime";
        if (effective.IsEnum) return effective.Name;
        return "String";
    }
}