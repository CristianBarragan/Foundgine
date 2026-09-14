using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class CostEstimateProvenanceTests
{
    [Fact]
    public void Heuristic_estimate_has_unknown_statistics_freshness()
    {
        var estimate = ProviderCostEstimate.From("test", 10d);

        Assert.Equal("heuristic", estimate.EffectiveProvenance.Source);
        Assert.Equal(CostStatisticsFreshness.Unknown, estimate.EffectiveProvenance.Freshness);
        Assert.Null(estimate.EffectiveProvenance.StatisticsVersion);
    }

    [Fact]
    public void Statistics_provenance_records_version_age_and_freshness()
    {
        var observed = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
        var estimated = observed.AddMinutes(5);
        var provenance = CostEstimateProvenance.FromStatistics(
            "postgresql.analyze",
            "stats-42",
            observed,
            estimated,
            TimeSpan.FromHours(1));

        Assert.Equal("stats-42", provenance.StatisticsVersion);
        Assert.Equal(TimeSpan.FromMinutes(5), provenance.StatisticsAge);
        Assert.Equal(CostStatisticsFreshness.Fresh, provenance.Freshness);
    }

    [Fact]
    public void Stale_statistics_are_explicitly_marked()
    {
        var observed = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        var estimated = observed.AddHours(25);
        var provenance = CostEstimateProvenance.FromStatistics(
            "postgresql.analyze",
            "stats-old",
            observed,
            estimated,
            TimeSpan.FromHours(24));

        Assert.Equal(CostStatisticsFreshness.Stale, provenance.Freshness);
        Assert.Equal(TimeSpan.FromHours(25), provenance.StatisticsAge);
    }

    [Fact]
    public void Future_statistics_observation_is_rejected()
    {
        var observed = new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero);
        var estimated = observed.AddMinutes(-1);

        Assert.Throws<ArgumentOutOfRangeException>(() => CostEstimateProvenance.FromStatistics(
            "postgresql.analyze", "stats", observed, estimated, TimeSpan.FromHours(1)));
    }
}