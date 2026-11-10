using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundgine.Providers.Orm.Generator;

/// <summary>
/// Emits, for every [FoundgineEntity] type, a strongly typed <c>OrmEntityMap&lt;T&gt;</c>: per-field getters,
/// predicate factories (==, !=, in) and ordering factories written as ordinary C# expression lambdas. The C#
/// compiler builds those expression trees, so no property discovery or Expression.Compile() happens at runtime.
/// Generated maps provide strongly typed metadata for the EF Core expression-tree adapter.
/// Field/entity naming deliberately mirrors Foundgine.Providers.Aot.Generator (attribute Name, else CLR name).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class OrmMapGenerator : IIncrementalGenerator
{
    private const string EntityAttribute = "Foundgine.Providers.Aot.FoundgineEntityAttribute";
    private const string FieldAttribute = "Foundgine.Providers.Aot.FoundgineFieldAttribute";
    private const string RelationshipAttribute = "Foundgine.Providers.Aot.FoundgineRelationshipAttribute";
    private const string MapBaseMetadataName = "Foundgine.Providers.Orm.OrmEntityMap`1";

    private static readonly SymbolDisplayFormat Annotated =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private sealed class FieldInfo
    {
        public string Property = "";
        public string Name = "";
        public string TypeAnnotated = "";
        public string TypePlain = "";
        public bool IsPrimaryKey;
        public bool Comparable;
        public bool Orderable;
        public bool Settable;
        public bool NeedsStringCompare;
    }

    private sealed class RelationshipInfo
    {
        public string Property = "";
        public string Name = "";
        public string TargetType = "";
    }

    private sealed class EntityInfo
    {
        public string TypeName = "";
        public string EntityName = "";
        public string MapName = "";
        public List<FieldInfo> Fields = new();
        public List<RelationshipInfo> Relationships = new();
        public bool HasDefaultCtor;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntityAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => Describe((INamedTypeSymbol)ctx.TargetSymbol))
            .Collect();

        context.RegisterSourceOutput(
            entities.Combine(context.CompilationProvider),
            static (spc, pair) =>
            {
                var (list, compilation) = pair;
                // Only act in projects that actually reference the ORM execution core.
                if (list.IsDefaultOrEmpty || compilation.GetTypeByMetadataName(MapBaseMetadataName) is null)
                    return;

                var ordered = list.OrderBy(x => x.TypeName, StringComparer.Ordinal).ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entity in ordered)
                {
                    if (!names.Add(entity.EntityName))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor("FGORM001", "Duplicate semantic entity name",
                                "Foundgine semantic entity name '{0}' is declared more than once.",
                                "Foundgine.Orm", DiagnosticSeverity.Error, true),
                            Location.None, entity.EntityName));
                        return;
                    }

                    spc.AddSource(entity.MapName + ".g.cs", EmitMap(entity));
                }

                spc.AddSource("GeneratedOrmMaps.g.cs", EmitIndex(ordered));
            });
    }

    private static EntityInfo Describe(INamedTypeSymbol type)
    {
        var info = new EntityInfo
        {
            TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityName = NamedString(Attr(type, EntityAttribute), "Name") ?? type.Name
        };
        info.MapName = Sanitize(info.TypeName) + "OrmMap";
        info.HasDefaultCtor = !type.IsAbstract && type.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

        foreach (var p in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (p.DeclaredAccessibility != Accessibility.Public || p.IsStatic || p.IsIndexer) continue;
            if (p.GetMethod is null || p.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;
            var relationship = Attr(p, RelationshipAttribute);
            if (relationship is not null)
            {
                var target = relationship.ConstructorArguments.Length > 0
                    ? relationship.ConstructorArguments[0].Value as INamedTypeSymbol : null;
                var enumerable = p.Type is INamedTypeSymbol collectionType
                    ? collectionType.AllInterfaces.Concat(new[] { collectionType })
                        .FirstOrDefault(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                    : null;
                if (target is not null && enumerable is not null)
                {
                    info.Relationships.Add(new RelationshipInfo
                    {
                        Property = p.Name,
                        Name = NamedString(relationship, "Name") ?? p.Name,
                        TargetType = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    });
                }
                continue;
            }

            var field = Attr(p, FieldAttribute);
            info.Fields.Add(new FieldInfo
            {
                Property = p.Name,
                Name = NamedString(field, "Name") ?? p.Name,
                TypeAnnotated = p.Type.ToDisplayString(Annotated),
                TypePlain = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsPrimaryKey = NamedBool(field, "IsPrimaryKey"),
                Comparable = p.Type is not IArrayTypeSymbol,
                Orderable = IsOrderable(p.Type),
                NeedsStringCompare = p.Type.SpecialType == SpecialType.System_String,
                Settable = p.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false }
            });
        }

        return info;
    }

    private static string EmitMap(EntityInfo e)
    {
        var sb = new StringBuilder();
        var T = e.TypeName;
        sb.AppendLine("// <auto-generated/> Foundgine ORM AOT map. Do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Foundgine.Generated.Orm");
        sb.AppendLine("{");
        sb.AppendLine($"    internal sealed class {e.MapName} : global::Foundgine.Providers.Orm.OrmEntityMap<{T}>");
        sb.AppendLine("    {");
        var create = e.HasDefaultCtor ? $"static () => new {T}()" : "null";
        sb.AppendLine($"        public {e.MapName}() : base(\"{Esc(e.EntityName)}\", {create})");
        sb.AppendLine("        {");

        foreach (var relationship in e.Relationships)
        {
            sb.AppendLine($"            AddRelationship(new global::Foundgine.Providers.Orm.OrmRelationshipBinding<{T}, {relationship.TargetType}>(\"{Esc(relationship.Name)}\", e => e.{relationship.Property}));");
        }

        foreach (var f in e.Fields)
        {
            var A = f.TypeAnnotated;
            var P = "e." + f.Property;
            var predicate = $"global::System.Linq.Expressions.Expression<global::System.Func<{T}, bool>>";
            var coerce = $"global::Foundgine.Providers.Orm.OrmValue.Coerce<{A}>";
            var notComparable = $"static _ => throw new global::System.NotSupportedException(\"Field '{Esc(f.Name)}' of type {Esc(f.TypePlain)} is not comparable in ORM predicates.\")";

            sb.AppendLine($"            Add(new global::Foundgine.Providers.Orm.OrmFieldBinding<{T}>(");
            sb.AppendLine($"                \"{Esc(f.Name)}\", typeof({f.TypePlain}), {(f.IsPrimaryKey ? "true" : "false")},");
            sb.AppendLine($"                static e => (object?){P},");
            sb.AppendLine($"                (global::System.Linq.Expressions.Expression<global::System.Func<{T}, object?>>)(e => (object?){P}),");
            if (f.Comparable)
            {
                sb.AppendLine($"                static v => {{ var c = {coerce}(v); {predicate} x = e => {P} == c; return x; }},");
                sb.AppendLine($"                static v => {{ var c = {coerce}(v); {predicate} x = e => {P} != c; return x; }},");
                sb.AppendLine($"                static vs => {{ var c = new global::System.Collections.Generic.List<{A}>(vs.Count); foreach (var i in vs) c.Add({coerce}(i)); {predicate} x = e => c.Contains({P}); return x; }},");
                sb.AppendLine($"                static (q, d) => d ? global::System.Linq.Queryable.OrderByDescending(q, e => {P}) : global::System.Linq.Queryable.OrderBy(q, e => {P}),");
                sb.AppendLine($"                static (q, d) => d ? global::System.Linq.Queryable.ThenByDescending(q, e => {P}) : global::System.Linq.Queryable.ThenBy(q, e => {P}),");
                if (f.Orderable)
                {
                    // Strings have no > operator in expression trees; string.Compare is translated by the ORMs.
                    var gt = f.NeedsStringCompare ? $"global::System.String.Compare({P}, c) > 0" : $"{P} > c";
                    var lt = f.NeedsStringCompare ? $"global::System.String.Compare({P}, c) < 0" : $"{P} < c";
                    sb.AppendLine($"                static v => {{ var c = {coerce}(v); {predicate} x = e => {gt}; return x; }},");
                    sb.AppendLine($"                static v => {{ var c = {coerce}(v); {predicate} x = e => {lt}; return x; }},");
                }
                else
                {
                    sb.AppendLine("                null,");
                    sb.AppendLine("                null,");
                }

                sb.AppendLine(f.Settable
                    ? $"                static (e, v) => {P} = {coerce}(v)));"
                    : "                null));");
            }
            else
            {
                sb.AppendLine($"                {notComparable},");
                sb.AppendLine($"                {notComparable},");
                sb.AppendLine($"                static _ => throw new global::System.NotSupportedException(\"Field '{Esc(f.Name)}' is not comparable.\"),");
                sb.AppendLine($"                static (_, _) => throw new global::System.NotSupportedException(\"Field '{Esc(f.Name)}' is not orderable.\"),");
                sb.AppendLine($"                static (_, _) => throw new global::System.NotSupportedException(\"Field '{Esc(f.Name)}' is not orderable.\"),");
                sb.AppendLine("                null,");
                sb.AppendLine("                null,");
                sb.AppendLine(f.Settable
                    ? $"                static (e, v) => {P} = {coerce}(v)));"
                    : "                null));");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EmitIndex(EntityInfo[] entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Foundgine ORM AOT map index. Do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Foundgine.Generated.Orm");
        sb.AppendLine("{");
        sb.AppendLine("    public static class GeneratedOrmMaps");
        sb.AppendLine("    {");
        sb.AppendLine("        public static global::System.Collections.Generic.IReadOnlyList<global::Foundgine.Providers.Orm.IOrmEntityMap> CreateAll() =>");
        sb.AppendLine("            new global::Foundgine.Providers.Orm.IOrmEntityMap[]");
        sb.AppendLine("            {");
        foreach (var e in entities) sb.AppendLine($"                new {e.MapName}(),");
        sb.AppendLine("            };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Types for which C# can build a relational operator inside an expression tree.</summary>
    private static bool IsOrderable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        if (type.TypeKind == TypeKind.Enum) return true;
        switch (type.SpecialType)
        {
            case SpecialType.System_Byte: case SpecialType.System_SByte:
            case SpecialType.System_Int16: case SpecialType.System_UInt16:
            case SpecialType.System_Int32: case SpecialType.System_UInt32:
            case SpecialType.System_Int64: case SpecialType.System_UInt64:
            case SpecialType.System_Single: case SpecialType.System_Double:
            case SpecialType.System_Decimal: case SpecialType.System_DateTime:
            case SpecialType.System_Char: case SpecialType.System_String:
                return true;
        }

        return type.ToDisplayString() is "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan";
    }

    private static AttributeData? Attr(ISymbol symbol, string name) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == name);

    private static string? NamedString(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value as string;

    private static bool NamedBool(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value is true;

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    private static string Esc(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
