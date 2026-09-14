namespace Foundgine.Core.Semantic.Planning;

/// <summary>Describes where a provider cost estimate came from and how current its statistics are.</summary>
public readonly record struct CostEstimateProvenance(
    string Source,
    string? StatisticsVersion,
    DateTimeOffset? EstimatedAtUtc,
    TimeSpan? StatisticsAge,
    CostStatisticsFreshness Freshness)
{
    public static CostEstimateProvenance
        Heuristic(string source = "heuristic", DateTimeOffset? estimatedAtUtc = null) =>
        new(source, null, estimatedAtUtc ?? DateTimeOffset.UtcNow, null, CostStatisticsFreshness.Unknown);

    public static CostEstimateProvenance FromStatistics(
        string source,
        string statisticsVersion,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? estimatedAtUtc = null,
        TimeSpan? staleAfter = null)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(statisticsVersion))
            throw new ArgumentException("Statistics version is required.", nameof(statisticsVersion));
        var now = estimatedAtUtc ?? DateTimeOffset.UtcNow;
        if (observedAtUtc > now) throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
        var age = now - observedAtUtc;
        var freshness = staleAfter is null
            ? CostStatisticsFreshness.Unknown
            : age <= TimeSpan.FromTicks(staleAfter.Value.Ticks / 2)
                ? CostStatisticsFreshness.Fresh
                : age <= staleAfter.Value
                    ? CostStatisticsFreshness.Aging
                    : CostStatisticsFreshness.Stale;
        return new CostEstimateProvenance(source, statisticsVersion, now, age, freshness);
    }
}

public enum CostStatisticsFreshness
{
    Unknown = 0,
    Fresh = 1,
    Aging = 2,
    Stale = 3
}