using System.Text;

namespace Foundgine.Core.Abstractions;

/// <summary>
/// Canonical semantic identity and stable hashing rules shared by runtime
/// identifiers and the AOT generator. The canonical key is intentionally
/// independent of CLR metadata tokens, declaration order, and runtime state.
/// </summary>
public static class SemanticIdentity
{
    public const string EntityNamespace = "entity";
    public const string FieldNamespace = "field";
    public const string RelationshipNamespace = "relationship";
    public const string TableNamespace = "table";
    public const string ColumnNamespace = "column";
    public const string ModelNamespace = "model";
    public const string ConnectionNamespace = "connection";
    public const string AuthorizationNamespace = "authorization";

    public static string EntityKey(string semanticName) =>
        Key(EntityNamespace, semanticName);

    public static string FieldKey(string semanticEntityName, string semanticFieldName) =>
        Key(FieldNamespace, Pair(semanticEntityName, semanticFieldName));

    public static string RelationshipKey(string semanticEntityName, string semanticRelationshipName) =>
        Key(RelationshipNamespace, Pair(semanticEntityName, semanticRelationshipName));

    public static string TableKey(string storageName) =>
        Key(TableNamespace, storageName);

    public static string ColumnKey(string storageName, string columnName) =>
        Key(ColumnNamespace, Pair(storageName, columnName));

    public static string ModelKey(string semanticName) =>
        Key(ModelNamespace, semanticName);

    public static string ConnectionKey(string semanticModelName, string semanticConnectionName) =>
        Key(ConnectionNamespace, Pair(semanticModelName, semanticConnectionName));

    public static string AuthorizationKey(string declaringType, string authorizationName) =>
        Key(AuthorizationNamespace, Pair(declaringType, authorizationName));

    /// <summary>Computes the Foundgine stable 64-bit identity hash.</summary>
    public static ulong Hash(string canonicalKey)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
            throw new ArgumentException("Canonical identity key is required.", nameof(canonicalKey));

        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;

        foreach (var b in Encoding.UTF8.GetBytes(canonicalKey))
        {
            hash ^= b;
            hash *= prime;
        }

        // Zero is reserved as an invalid/unassigned identity.
        return hash == 0 ? 1UL : hash;
    }

    public static string Normalize(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identity component is required.", parameterName);

        return value.Trim();
    }

    public static ulong ValidateExplicitId(ulong value, string description) =>
        value == 0
            ? throw new ArgumentOutOfRangeException(nameof(value), value,
                $"Explicit {description} identity 0 is reserved and cannot be assigned.")
            : value;

    private static string Key(string @namespace, string value) =>
        @namespace + ":" + Normalize(value, nameof(value));

    private static string Pair(string left, string right) =>
        Normalize(left, nameof(left)) + "." + Normalize(right, nameof(right));
}
