using Foundgine.Core.Execution;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Supplies the runtime pagination values expected by the parameterized SQL
/// provider when tests execute compiled plans directly instead of through
/// FoundgineEngine. The provider adds one lookahead row for forward pagination.
/// </summary>
internal static class PaginationExecutionContext
{
    public static ExecutionContext Create(int limit, string? after = null, int? offset = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ExecutionContextKeys.PaginationLimit] = limit,
            [ExecutionContextKeys.PaginationHasCursor] = after is not null
        };

        if (offset is not null)
            values[ExecutionContextKeys.PaginationOffset] = offset.Value;

        return new ExecutionContext(values);
    }
}