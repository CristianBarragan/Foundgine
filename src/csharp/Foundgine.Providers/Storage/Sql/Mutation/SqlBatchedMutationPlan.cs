using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Providers.Storage.Sql.Query;

namespace Foundgine.Providers.Storage.Sql.Mutation;

/// <summary>
/// A provider-specific PostgreSQL mutation plan containing one physical SQL
/// command for the entire logical mutation batch.
/// </summary>
public sealed record SqlBatchedMutationPlan(
    string CommandText,
    IReadOnlyList<SqlParameterBinding> Parameters,
    IReadOnlyList<BatchedGroupMeta> Groups,
    IReadOnlyList<BatchedOperationRowKey> RowKeys,
    IReadOnlyList<MutationDependency> Dependencies)
    : ProviderMutationBatchPlan(BuildOperations(RowKeys.Count))
{
    public int OperationCount => RowKeys.Count;

    private static IReadOnlyList<ProviderMutationPlan> BuildOperations(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => (ProviderMutationPlan)new SqlMutationPlan("", [], []))
            .ToArray();
}

/// <summary>
/// Correlates one physical result row to an original mutation operation.
/// Ordinal is the 1-based position inside the group's unnest arrays.
/// </summary>
public sealed record BatchedOperationRowKey(
    int OperationIndex,
    int GroupId,
    int Ordinal);

/// <summary>
/// Metadata required by the batched executor to reconstruct the original
/// per-operation MutationResult list.
/// </summary>
public sealed record BatchedGroupMeta(
    int GroupId,
    IReadOnlyList<int> OperationIndexesByOrdinal,
    bool IsOrdinalAddressable,
    IReadOnlyDictionary<FieldId, Type> ReturnedFieldTypes);
