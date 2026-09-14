using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Aot;

/// <summary>
/// Fluent semantic operations for source-generated application fields.
/// </summary>
public static class GeneratedSemanticFieldExtensions
{
    public static SemanticFieldFilter Eq(this GeneratedSemanticField field, object? value) =>
        new(field.Id, SemanticFilterOperator.Eq, value);

    public static SemanticFieldFilter Neq(this GeneratedSemanticField field, object? value) =>
        new(field.Id, SemanticFilterOperator.Neq, value);

    public static SemanticFieldFilter In(this GeneratedSemanticField field, params object?[] values) =>
        new(field.Id, SemanticFilterOperator.In, values);

    public static SemanticMutationField Set(this GeneratedSemanticField field, object? value) =>
        new(field.Id, value);

    public static SemanticOrderTerm Asc(this GeneratedSemanticField field) =>
        new(field.Id, SemanticSortDirection.Asc);

    public static SemanticOrderTerm Desc(this GeneratedSemanticField field) =>
        new(field.Id, SemanticSortDirection.Desc);
}
