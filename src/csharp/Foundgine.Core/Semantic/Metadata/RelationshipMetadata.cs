using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// Static domain relationship metadata. The key mapping identifies how the
/// related domain entities correlate; provider-specific join behavior is
/// deliberately not part of the semantic relationship contract.
/// </summary>
public sealed record RelationshipMetadata(
    RelationshipId Id,
    EntityId Source,
    EntityId Target,
    string Name,
    ColumnReference SourceKey,
    ColumnReference TargetKey,
    bool IsCollection = true,
    IReadOnlyList<AliasDeclaration>? Aliases = null);