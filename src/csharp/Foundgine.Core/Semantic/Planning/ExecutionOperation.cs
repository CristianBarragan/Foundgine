namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Provider-neutral logical operations used by the read execution algebra.
/// CRUD mutation kinds intentionally do not belong here.
/// </summary>
public enum ExecutionOperation
{
    Scan,
    Traverse,
    TraverseConnection
}
