using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Storage.Sql;

/// <summary>
/// Physical SQL representation of a provider-independent execution plan.
/// SQL exists only at this provider boundary.
/// </summary>
public sealed record SqlPlan(
    string CommandText,
    IReadOnlyList<SqlColumnBinding> Columns,
    IReadOnlyList<Foundgine.Providers.Storage.Sql.Query.SqlParameterBinding>? Parameters = null,
    SqlPaginationPlan? Pagination = null,
    IReadOnlyList<SqlAuthorizationPredicate>? Authorization = null) : ProviderPlan("sql")
{
    public IReadOnlyList<Foundgine.Providers.Storage.Sql.Query.SqlParameterBinding> EffectiveParameters =>
        Parameters ?? [];
}

public sealed record SqlColumnBinding(
    string ResultName,
    EntityId EntityId,
    FieldId FieldId,
    string ColumnName,
    int NodeId,
    bool IsCursor = false);

/// <summary>
/// Forward keyset pagination metadata. CursorValues describes the exact
/// semantic ordering tuple used by the SQL seek predicate.
/// </summary>
public sealed record SqlPaginationPlan(
    int First,
    IReadOnlyList<SqlCursorBinding> CursorValues,
    string? After);

public sealed record SqlCursorBinding(
    string ResultName,
    EntityId EntityId,
    FieldId FieldId,
    Type ClrType,
    SemanticSortDirection Direction);

/// <summary>
/// Authorization predicates carried into the SQL provider plan. The predicate
/// remains provider-independent here; SQL lowering can bind its members and
/// context values without retaining an expression tree or delegate.
/// </summary>
public sealed record SqlAuthorizationPredicate(
    int NodeId,
    AuthorizationPredicate Predicate);