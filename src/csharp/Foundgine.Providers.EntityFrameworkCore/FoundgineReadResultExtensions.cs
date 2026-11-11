using Foundgine.Core.Execution;

namespace Foundgine.Providers.EntityFrameworkCore;

public static class FoundgineReadResultExtensions
{
    /// <summary>The first row, or KeyNotFoundException with the given message.</summary>
    public static ExecutionRow FirstOrThrow(this FoundgineReadResult result, string notFound) =>
        result.Rows.FirstOrDefault() ?? throw new KeyNotFoundException(notFound);
}
