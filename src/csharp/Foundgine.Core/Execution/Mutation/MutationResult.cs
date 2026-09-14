using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Execution.Mutation;

public sealed record MutationResult(
    int AffectedRows,
    IReadOnlyDictionary<FieldId, object?>? ReturnedValues = null)
{
    // Compatibility name used by the PostgreSQL E2E integration tests and older
    // callers. ReturnedValues remains the canonical result representation.
    public IReadOnlyDictionary<FieldId, object?>? ReturnedFields => ReturnedValues;
}