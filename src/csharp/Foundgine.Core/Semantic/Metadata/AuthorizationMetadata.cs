using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>
/// A compile-time authorization predicate attached to a semantic connection.
/// The expression is descriptive metadata; providers may later lower the
/// predicate into their native authorization/filter representation.
/// </summary>
public sealed record AuthorizationMetadata(
    AuthorizationId Id,
    ConnectionId ConnectionId,
    string Name,
    string SourceMember,
    Type ContextType,
    Type ResourceType,
    string Expression,
    AuthorizationPredicate? Predicate = null);