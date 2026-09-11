using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql;
using Foundgine.E2E.Tests.Banking;
using Xunit;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Proves that the AggregateExecutionStrategy hint set by
/// AggregateCardinalityOptimizationRule is actually consumed downstream: it used to be
/// dropped silently at the SemanticPlan -> ExecutionIR lowering boundary and never reached
/// any provider compiler. These tests exercise the full path (rule -> ExecutionIRCompiler ->
/// SqlCompiler) so a future regression that drops the hint again fails loudly here rather
/// than only being visible as a missed optimization at runtime.
/// </summary>
public sealed class AggregateExistenceSqlRenderingTests
{
    [Fact]
    public void CountGreaterThanZero_CompilesToExistsInsteadOfCount()
    {
        var plan = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(
            new SemanticAggregateFilter(
                BankingSemanticModel.CustomerAccounts, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 0)));

        var sql = Compile(plan).CommandText;

        Assert.Contains("EXISTS (SELECT 1 FROM", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CountEqualZero_CompilesToNotExistsInsteadOfCount()
    {
        var plan = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(
            new SemanticAggregateFilter(
                BankingSemanticModel.CustomerAccounts, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Eq, 0)));

        var sql = Compile(plan).CommandText;

        Assert.Contains("NOT EXISTS (SELECT 1 FROM", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(*)", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SemanticAggregateFilterOperator.Gte, 0)]
    [InlineData(SemanticAggregateFilterOperator.Lt, 0)]
    public void ConstantCountComparisons_AreNotMiscompiledAsExistenceTests(
        SemanticAggregateFilterOperator op, long value)
    {
        // COUNT >= 0 is always true and COUNT < 0 is always false for a non-negative COUNT.
        // Neither is equivalent to EXISTS/NOT EXISTS, so the optimizer must leave the
        // aggregate strategy at Default rather than applying an existence rewrite.
        var plan = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(
            new SemanticAggregateFilter(
                BankingSemanticModel.CustomerAccounts, SemanticFilterAggregate.Count, null,
                op, value)));

        Assert.Equal(AggregateExecutionStrategy.Default, plan.Root.AggregateExecutionStrategy);

        var sql = Compile(plan).CommandText;

        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CountGreaterThanOne_IsNotEligible_StillCompilesToCount()
    {
        // The rule never assigns a strategy to this comparison (it genuinely depends on the
        // exact count), so AggregateExecutionStrategy stays Default and the SQL writer must
        // fall back to the original COUNT-subquery rendering.
        var plan = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(
            new SemanticAggregateFilter(
                BankingSemanticModel.CustomerAccounts, SemanticFilterAggregate.Count, null,
                SemanticAggregateFilterOperator.Gt, 1)));

        Assert.Equal(AggregateExecutionStrategy.Default, plan.Root.AggregateExecutionStrategy);

        var sql = Compile(plan).CommandText;

        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutRunningTheRule_DefaultStrategyStillCompilesToCount()
    {
        // Compiling the un-optimized plan directly (strategy left at its Default) must
        // preserve the pre-existing COUNT-subquery output exactly, so provider output for
        // callers that never run the optimizer does not silently change.
        var plan = CreatePlan(new SemanticAggregateFilter(
            BankingSemanticModel.CustomerAccounts, SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gt, 0));

        var sql = Compile(plan).CommandText;

        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTS", sql, StringComparison.Ordinal);
    }

    private static SqlPlan Compile(SemanticPlan plan) =>
        new SqlCompiler(BankingRelationalMetadata.Build()).Compile(plan);

    private static SemanticPlan CreatePlan(SemanticFilterExpression filter) =>
        new(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                BankingSemanticModel.Customer,
                [new FieldId(1)],
                null,
                null,
                [],
                new SemanticQueryOptions(Filter: filter)),
            AuthorizationBinding: new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
}