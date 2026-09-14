namespace Foundgine.Core.Execution;

/// <summary>Reserved runtime context keys used by provider execution for dynamic request values.</summary>
public static class ExecutionContextKeys
{
    public const string PaginationLimit = "foundgine.pagination.limit";
    public const string PaginationOffset = "foundgine.pagination.offset";
    public const string PaginationHasCursor = "foundgine.pagination.hasCursor";
}
