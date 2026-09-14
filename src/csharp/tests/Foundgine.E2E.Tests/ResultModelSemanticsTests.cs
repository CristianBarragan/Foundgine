using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Results;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class ResultModelSemanticsTests
{
    [Fact]
    public void Materializer_returns_the_canonical_semantic_result_type()
    {
        var customer = new EntityId(1);
        var id = new FieldId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(id, "Id")
                .Field(id, "Id", typeof(long)))
            .Build();

        var plan = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, customer, [id], null, []));

        var row = new ExecutionRow(
            new Dictionary<string, object?>(),
            new Dictionary<ExecutionCellKey, object?>
            {
                [new ExecutionCellKey(1, customer, id)] = 42L
            });

        var result = new ResultMaterializer(model).Materialize(
            plan, new ExecutionResult([row]));

        Assert.IsType<SemanticResult>(result);
        Assert.IsType<SemanticResultNode>(result.Roots[0]);
    }

    [Fact]
    public void Identity_is_not_required_in_the_projection_to_reconstruct_unique_nodes()
    {
        var customer = new EntityId(1);
        var id = new FieldId(1);
        var name = new FieldId(2);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(id, "Id")
                .Field(id, "Id", typeof(long))
                .Field(name, "Name", typeof(string)))
            .Build();

        var plan = new SemanticPlan(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                customer,
                [name],
                null,
                []));

        ExecutionRow Row(long identity, string value) => new(
            new Dictionary<string, object?>(),
            new Dictionary<ExecutionCellKey, object?>
            {
                [new ExecutionCellKey(1, customer, id)] = identity,
                [new ExecutionCellKey(1, customer, name)] = value
            });

        var result = new ResultMaterializer(model).Materialize(
            plan,
            new ExecutionResult([Row(1, "Ada"), Row(2, "Grace"), Row(1, "Ada")]));

        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(1L, result.Roots[0].IdentityValue);
        Assert.Equal(2L, result.Roots[1].IdentityValue);
        Assert.DoesNotContain(id, result.Roots[0].Values.Keys);
        Assert.Equal("Ada", result.Roots[0].Values[name]);
    }

    [Fact]
    public void Materialization_preserves_execution_page_info_and_evidence()
    {
        var customer = new EntityId(1);
        var id = new FieldId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(id, "Id")
                .Field(id, "Id", typeof(long)))
            .Build();

        var plan = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, customer, [id], null, []));

        var pageInfo = new ExecutionPageInfo("start", "end", true, false);
        var evidence = new ExecutionEvidence("sql", "plan", [1], 1, 3);
        var row = new ExecutionRow(
            new Dictionary<string, object?>(),
            new Dictionary<ExecutionCellKey, object?>
            {
                [new ExecutionCellKey(1, customer, id)] = 42L
            });

        var materialized = new ResultMaterializer(model).Materialize(
            plan,
            new ExecutionResult([row], pageInfo, evidence));

        Assert.Equal(
            new SemanticResultPageInfo("start", "end", true, false),
            materialized.PageInfo);
        Assert.NotNull(materialized.Evidence);
        Assert.Equal("sql", materialized.Evidence!.Provider);
        Assert.Equal("plan", materialized.Evidence.PlanFingerprint);
        Assert.Equal([1], materialized.Evidence.AuthorizedNodeIds);
        Assert.Equal(1, materialized.Evidence.RowsReturned);
        Assert.Equal(3, materialized.Evidence.ElapsedMilliseconds);
        Assert.Null(materialized.Evidence.ProviderOperationFingerprint);
        Assert.Null(materialized.Evidence.IntentFingerprint);
        Assert.Null(materialized.Evidence.AuthorizationFingerprint);
    }
}