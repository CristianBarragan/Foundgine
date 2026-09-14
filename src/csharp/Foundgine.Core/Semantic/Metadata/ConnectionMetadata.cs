using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// A compile-time semantic connection from a model to a known entity.
/// The connection describes what may be visited; it does not materialize or
/// populate either side. Relational key details remain owned by the entity
/// metadata/EF model.
/// </summary>
public sealed record ConnectionMetadata(
    ConnectionId Id,
    ModelId Source,
    EntityId Target,
    string Name,
    string SourceMember,
    IReadOnlyList<ConnectionFieldMetadata>? Fields = null);