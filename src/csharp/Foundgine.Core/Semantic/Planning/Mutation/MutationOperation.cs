using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Planning.Mutation;

public sealed record MutationOperation(
    MutationEntitySchema Entity,
    MutationKind Kind,
    IReadOnlyList<MutationFieldValue> Fields,
    SemanticFilterExpression? Filter,
    IReadOnlyList<ColumnId>? ConflictColumns = null,
    IReadOnlyList<FieldId>? ReturnFields = null);