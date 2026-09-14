using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Core.Semantic.Query;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class OpenIntentApiTests
{
    private sealed class Customer
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string TenantId { get; init; } = "";
        public IReadOnlyList<Order> Orders { get; init; } = [];
    }

    private sealed class Order
    {
        public int Id { get; init; }
        public DateOnly OrderDate { get; init; }
    }

    [Fact]
    public void Typed_query_is_open_and_produces_provider_neutral_intent()
    {
        var engine = new RecordingFoundgine();
        var tenantId = "tenant-a";

        var intent = engine
            .Query<Customer>()
            .Select(c => new { c.Id, c.Name })
            .Include(c => c.Orders, orders => orders
                .Select(o => new { o.Id, o.OrderDate }))
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .Take(25)
            .ToIntent();

        Assert.Equal("Customer", intent.RootEntity);
        Assert.Equal(3, intent.Selections.Count);
        Assert.Equal("Id", intent.Selections[0].Field);
        Assert.Equal("Name", intent.Selections[1].Field);
        Assert.Equal("Orders", intent.Selections[2].Relationship);
        Assert.Equal(2, intent.Selections[2].EffectiveChildren.Count);
        Assert.Equal(25, intent.Limit);
        Assert.IsType<Foundgine.Core.Semantic.Intent.ReadFieldFilter>(intent.Filter);
    }

    [Fact]
    public void Dynamic_query_remains_open_without_bypassing_structured_intent()
    {
        var engine = new RecordingFoundgine();

        var intent = engine
            .Query("Customer")
            .Select("Id", "Name")
            .Include("Orders", orders => orders.Select("Id", "OrderDate"))
            .Where("TenantId", SemanticFilterOperator.Eq, "tenant-a")
            .ToIntent();

        Assert.Equal("Customer", intent.RootEntity);
        Assert.Equal("Orders", intent.Selections[2].Relationship);
        Assert.Equal("TenantId", Assert.IsType<Foundgine.Core.Semantic.Intent.ReadFieldFilter>(intent.Filter).Field);
    }

    [Fact]
    public void Dynamic_typo_is_rejected_by_semantic_resolution_before_execution()
    {
        var model = new SemanticModelBuilder()
            .Entity<Customer>(new EntityId(1), "Customer", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Build();

        var intent = new RecordingFoundgine()
            .Query("Customer")
            .Select("Nmae")
            .ToIntent();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new Foundgine.Core.Semantic.Intent.ReadIntentCompiler(model).Compile(intent));

        Assert.Contains("Unknown field 'Customer.Nmae'", error.Message);
    }

    private sealed class RecordingFoundgine : IFoundgine
    {
        public SemanticAuthorizationCapabilities DescribeCapabilities() => throw new NotSupportedException();
        public SemanticCapabilityContract DescribeCapabilityContract() => throw new NotSupportedException();

        public SemanticCapabilityContract DescribeCapabilityContract(SecurityExecutionContext security) =>
            throw new NotSupportedException();

        public SemanticVersionSet DescribeVersionSet() => throw new NotSupportedException();
        public DryRunResult DryRun(SemanticRequest request) => throw new NotSupportedException();

        public PlanApproval ApprovePlan(SemanticRequest request, string approvedBy) =>
            throw new NotSupportedException();

        public Task<ExecutionResult> ExecuteApprovedAsync(PlanApproval approval, ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ExecutionResult> ExecuteAsync(SemanticRequest request, ExecutionContext? context = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ExecutionResult> ExecuteAsync(Foundgine.Core.Semantic.Intent.ReadIntent intent,
            ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}