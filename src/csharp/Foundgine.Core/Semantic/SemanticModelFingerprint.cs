using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Produces a canonical, provider-independent fingerprint for a semantic model.
/// The fingerprint is based only on semantic declarations and is independent of
/// registration/declaration order or CLR metadata identity.
/// </summary>
public static class SemanticModelFingerprint
{
    public static string Compute(SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var canonical = new StringBuilder("foundgine.semantic-contract.v1\n");

        foreach (var entity in model.Entities.OrderBy(x => x.Id.Value))
        {
            Append(canonical, "entity", entity.Id.Value);
            Append(canonical, "name", entity.Name);
            Append(canonical, "identity", entity.Identity.FieldId.Value, entity.Identity.Name);
            AppendAliases(canonical, entity.EffectiveAliases);

            foreach (var field in entity.Fields.OrderBy(x => x.Id.Value))
            {
                Append(canonical, "field", field.Id.Value);
                Append(canonical, "field-name", field.Name);
                Append(canonical, "field-type", CanonicalType(field.EffectiveSemanticType));
                Append(canonical, "field-capabilities", (byte)field.Capabilities);
                Append(canonical, "field-nullable", field.IsNullable);
                AppendAliases(canonical, field.EffectiveAliases);

                foreach (var constraint in field.EffectiveConstraints
                             .OrderBy(x => x.Kind)
                             .ThenBy(x => x.Value, StringComparer.Ordinal)
                             .ThenBy(x => x.Minimum)
                             .ThenBy(x => x.Maximum))
                {
                    Append(canonical, "constraint", (byte)constraint.Kind, constraint.Value ?? "",
                        constraint.Minimum?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                        constraint.Maximum?.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) ?? "");
                }
            }

            foreach (var relationship in entity.Relationships.OrderBy(x => x.Id.Value))
            {
                Append(canonical, "relationship", relationship.Id.Value, relationship.Name,
                    relationship.Target.Value, (byte)relationship.Cardinality);
                AppendAliases(canonical, relationship.EffectiveAliases);
            }
        }

        foreach (var traversal in model.Traversals
                     .OrderBy(x => x.Source.Value)
                     .ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            Append(canonical, "traversal", traversal.Source.Value, traversal.Name, traversal.Target.Value);
            foreach (var relationshipId in traversal.Path)
                Append(canonical, "traversal-edge", relationshipId.Value);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void AppendAliases(StringBuilder builder, IReadOnlyList<SemanticAlias> aliases)
    {
        foreach (var alias in aliases.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Name, StringComparer.Ordinal))
            Append(builder, "alias", alias.Name,
                alias.Weight?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
    }

    private static void Append(StringBuilder builder, string kind, params object[] values)
    {
        builder.Append(kind);
        foreach (var value in values)
        {
            var text = value.ToString() ?? "";
            builder.Append('|').Append(text.Length).Append(':').Append(text);
        }

        builder.Append('\n');
    }

    private static string CanonicalType(SemanticType type) => type switch
    {
        SemanticType.Scalar scalar => $"scalar:{scalar.Kind}",
        SemanticType.Enum value => $"enum:{value.Name}",
        SemanticType.Object value => $"object:{value.Name}",
        SemanticType.Collection value => $"collection:{CanonicalType(value.ElementType)}",
        _ => throw new InvalidOperationException($"Unsupported semantic type '{type.GetType().FullName}'.")
    };
}