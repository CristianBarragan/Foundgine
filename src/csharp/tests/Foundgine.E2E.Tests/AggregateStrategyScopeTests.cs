using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql;
using Foundgine.E2E.Tests.Banking;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class AggregateStrategyScopeTests
{
    [Fact]
    public void CardinalityStrategy_IgnoresAggregatesInsideRelationshipPredicates()
    {
        var nested = new SemanticAggregateFilter(
            BankingSemanticModel.AccountTransactions,
            SemanticFilterAggregate.Count,
            null,
            SemanticAggregateFilterOperator.Gt,
            0);

        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(
                BankingSemanticModel.CustomerAccounts,
                SemanticFilterAggregate.Count,
                null,
                SemanticAggregateFilterOperator.Gt,
                0),
            new SemanticRelationshipFilter(
                BankingSemanticModel.CustomerAccounts,
                SemanticRelationshipQuantifier.Some,
                nested)
        ]);

        var optimized = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(filter));

        // The root hint is justified by the root aggregate only. The nested aggregate
        // belongs to the relationship predicate's target scope and must not influence it.
        Assert.Equal(
            AggregateExecutionStrategy.CountExistsShortCircuit,
            optimized.Root.AggregateExecutionStrategy);
    }

    [Fact]
    public void SqlCompiler_DoesNotApplyRootStrategyToNestedAggregate()
    {
        var nested = new SemanticAggregateFilter(
            BankingSemanticModel.AccountTransactions,
            SemanticFilterAggregate.Count,
            null,
            SemanticAggregateFilterOperator.Gt,
            0);

        var filter = new SemanticAndFilter([
            new SemanticAggregateFilter(
                BankingSemanticModel.CustomerAccounts,
                SemanticFilterAggregate.Count,
                null,
                SemanticAggregateFilterOperator.Gt,
                0),
            new SemanticRelationshipFilter(
                BankingSemanticModel.CustomerAccounts,
                SemanticRelationshipQuantifier.Some,
                nested)
        ]);

        var plan = new AggregateCardinalityOptimizationRule().Apply(CreatePlan(filter));
        var sql = new SqlCompiler(BankingRelationalMetadata.Build()).Compile(plan).CommandText;

        // The root aggregate may use EXISTS, but the nested aggregate must remain a COUNT
        // because the root node's execution hint does not prove anything about that scope.
        Assert.Contains("EXISTS (SELECT 1 FROM", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
    }

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
