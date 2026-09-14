using Foundgine.Core.Semantic.Planning.Mutation;

namespace Foundgine.Providers.Storage.Sql.Query;

public sealed record SqlParameterBinding(
    string Name,
    object? Value,
    MutationValueReference? Source = null,
    string? ContextPath = null,
    Type? ClrType = null);
