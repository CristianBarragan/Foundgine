using Foundgine.Core.Semantic;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Provider-neutral description of the GraphQL schema exposed by the adapter.
/// It contains GraphQL-facing names and CLR-derived scalar types, but no
/// Hot Chocolate runtime objects. Hosts can use the descriptor to construct a
/// real Hot Chocolate schema, whose standard introspection system then exposes
/// __schema and __type.
/// </summary>
public sealed record GraphQLSchemaDescriptor(
    IReadOnlyList<GraphQLObjectTypeDescriptor> QueryTypes,
    IReadOnlyList<GraphQLObjectTypeDescriptor> MutationTypes,
    IReadOnlyList<GraphQLInputTypeDescriptor> InputTypes);

public sealed record GraphQLObjectTypeDescriptor(
    string Name,
    IReadOnlyList<GraphQLFieldDescriptor> Fields);

public sealed record GraphQLFieldDescriptor(
    string Name,
    string Type,
    bool IsList = false,
    bool IsNonNull = false,
    IReadOnlyList<GraphQLArgumentDescriptor>? Arguments = null);

public sealed record GraphQLArgumentDescriptor(
    string Name,
    string Type,
    bool IsList = false,
    bool IsNonNull = false);

public sealed record GraphQLInputTypeDescriptor(
    string Name,
    IReadOnlyList<GraphQLInputFieldDescriptor> Fields);

public sealed record GraphQLInputFieldDescriptor(
    string Name,
    string Type,
    bool IsList = false,
    bool IsNonNull = false);
