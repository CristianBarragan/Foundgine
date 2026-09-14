namespace Foundgine.SupplyChain.Advanced.Application;

/// <summary>
/// Application execution limits for the Supply Chain showcase.
/// These are operational/security policy, not structural metadata and not
/// generated semantic topology.
/// </summary>
public static class SupplyChainExecutionLimits
{
    public const int RecursiveBomMaxDepth = 5;
    public const int MaximumPageSize = 50;
    public const int MaximumTraversalNodes = 10000;
}