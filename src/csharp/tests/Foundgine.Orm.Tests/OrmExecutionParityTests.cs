using System.Linq.Expressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using Foundgine.Generated.Orm;
using Foundgine.Providers.Orm;
using Xunit;
using static Foundgine.Orm.Tests.OrmTestModel;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Orm.Tests;

/// <summary>
/// Provider-independent behaviour of the generated-composition core, run over LINQ-to-Objects so the
/// semantics (authorization, filters, ordering, paging, field visibility) are pinned down without a database.
/// The EF Core integration tests assert the same results against a real ORM provider.
/// </summary>
public sealed class OrmExecutionParityTests
{
    private sealed class EnumerableSource(IEnumerable<object> data) : IOrmQuerySource
    {
        public IQueryable<T> Query<T>() where T : class => data.OfType<T>().AsQueryable();
        public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class =>
            Task.FromResult<IReadOnlyList<T>>(query.ToList());
    }

    private sealed class CountingSource : IOrmQuerySource
    {
        public int QueryCount { get; private set; }
        public IQueryable<T> Query<T>() where T : class
        {
            QueryCount++;
            return Array.Empty<T>().AsQueryable();
        }

        public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class =>
            Task.FromResult<IReadOnlyList<T>>(query.ToList());
    }

    internal static OrmRegistry Registry() => OrmRegistry.Create(AllMetadata(), GeneratedOrmMaps.CreateAll(), Relationships());

    private static async Task<ExecutionResult> RunAsync(ExecutionIR ir, ExecutionContext context)
    {
        var registry = Registry();
        var plan = new OrmPlanCompiler("test-orm", registry).Compile(ir);
        return await new OrmExecutionProvider("test-orm", new EnumerableSource(SeedAll()), registry)
            .ExecuteAsync(plan, context);
    }


    [Theory]
    [InlineData(SemanticRelationshipQuantifier.Some, "A", new[] { "Ada" })]
    [InlineData(SemanticRelationshipQuantifier.None, "A", new[] { "Grace", "Linus" })]
    [InlineData(SemanticRelationshipQuantifier.All, "A", new[] { "Grace", "Linus" })]
    public async Task Relationship_quantifiers_translate_and_apply_related_authorization(
        SemanticRelationshipQuantifier quantifier, string sku, string[] expected)
    {
        var orders = SeedOrders();
        var customers = new[]
        {
            new OrmCustomer { Id = 1, TenantId = TenantA, Name = "Ada", Orders = orders.Where(x => x.CustomerId == 1).ToList() },
            new OrmCustomer { Id = 2, TenantId = TenantA, Name = "Grace", Orders = orders.Where(x => x.CustomerId == 2).ToList() },
            new OrmCustomer { Id = 3, TenantId = TenantA, Name = "Linus", Orders = orders.Where(x => x.CustomerId == 3).ToList() },
            new OrmCustomer { Id = 4, TenantId = TenantB, Name = "Eve", Orders = orders.Where(x => x.CustomerId == 4).ToList() }
        };
        var relationshipAuthorization = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            AuthorizationPredicate.Member(AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId, Sku], authorization: relationshipAuthorization);
        var filter = new SemanticRelationshipFilter(CustomerOrders, quantifier,
            new SemanticFieldFilter(Sku, SemanticFilterOperator.Eq, sku));
        var ir = Ir(new SemanticQueryOptions(filter), children: [child]);
        var capabilities = OrmProviderCapabilities.SharedLinq("test-orm") with
        {
            ContractVersion = "3",
            Features = OrmProviderCapabilities.SharedLinq("test-orm").Features | OrmProviderFeature.RelationshipFilters
        };
        var registry = Registry();
        var plan = new OrmPlanCompiler("test-orm", registry, capabilities).Compile(ir);
        var context = new ExecutionContext(new Dictionary<string, object?>
        {
            ["ctx.TenantId"] = TenantA,
            ["ctx.CustomerId"] = 1
        });
        var result = await new OrmExecutionProvider("test-orm", new EnumerableSource([.. customers, .. orders]), registry)
            .ExecuteAsync(plan, context);

        Assert.Equal(expected, result.Rows.Select(row => (string)row.Values["Name"]!).OrderBy(x => x));
    }

    [Fact]
    public void Aggregate_expression_uses_compile_time_selected_linq_method_handles()
    {
        var binding = new OrmRelationshipBinding<OrmCustomer, OrmOrder>("Orders", customer => customer.Orders);
        Expression<Func<OrmOrder, bool>> predicate = order => order.CustomerId == 1;
        Expression<Func<OrmOrder, decimal>> selector = order => order.Total;

        var min = binding.BuildAggregate(predicate, SemanticFilterAggregate.Min, selector,
            SemanticAggregateFilterOperator.Gt, 10m);
        var max = binding.BuildAggregate(predicate, SemanticFilterAggregate.Max, selector,
            SemanticAggregateFilterOperator.Lt, 100m);
        var count = binding.BuildAggregate(predicate, SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gte, 1);

        Assert.Contains("Min", min.ToString(), StringComparison.Ordinal);
        Assert.Contains("Max", max.ToString(), StringComparison.Ordinal);
        Assert.Contains("Count", count.ToString(), StringComparison.Ordinal);

        // A correlated MIN over no visible children is NULL, not an exception or a
        // fabricated zero. SQL WHERE semantics exclude that row for MIN > 10.
        var minPredicate = Assert.IsAssignableFrom<Expression<Func<OrmCustomer, bool>>>(min);
        Assert.False(minPredicate.Compile()(new OrmCustomer { Id = 99, Orders = [] }));
    }

    [Fact]
    public void Aggregate_count_order_uses_authorized_collection_count_expression()
    {
        var binding = new OrmRelationshipBinding<OrmCustomer, OrmOrder>("Orders", customer => customer.Orders);
        Expression<Func<OrmOrder, bool>> authorizedOrders = order => order.CustomerId == 1;
        var customers = new[]
        {
            new OrmCustomer { Id = 1, Name = "Ada", Orders = [new OrmOrder { Id = 1, CustomerId = 1 }, new OrmOrder { Id = 2, CustomerId = 1 }] },
            new OrmCustomer { Id = 2, Name = "Grace", Orders = [new OrmOrder { Id = 3, CustomerId = 2 }] },
            new OrmCustomer { Id = 3, Name = "Linus", Orders = [] }
        }.AsQueryable();

        var ordered = binding.ApplyAggregateOrder(customers, authorizedOrders,
            SemanticOrderAggregate.Count, null, descending: true, thenBy: false);

        Assert.Equal(new[] { "Ada", "Grace", "Linus" }, ordered.Select(x => x.Name));
    }

    [Fact]
    public void Aggregate_min_max_order_preserves_empty_set_null_semantics()
    {
        var binding = new OrmRelationshipBinding<OrmCustomer, OrmOrder>("Orders", customer => customer.Orders);
        Expression<Func<OrmOrder, bool>> predicate = _ => true;
        Expression<Func<OrmOrder, decimal>> selector = order => order.Total;
        var customers = new[]
        {
            new OrmCustomer { Id = 1, Name = "Ada", Orders = [new OrmOrder { Id = 1, Total = 10m }, new OrmOrder { Id = 2, Total = 20m }] },
            new OrmCustomer { Id = 2, Name = "Grace", Orders = [new OrmOrder { Id = 3, Total = 30m }] },
            new OrmCustomer { Id = 3, Name = "Linus", Orders = [] }
        }.AsQueryable();

        var min = binding.ApplyAggregateOrder(customers, predicate, SemanticOrderAggregate.Min, selector,
            descending: false, thenBy: false);
        var max = binding.ApplyAggregateOrder(customers, predicate, SemanticOrderAggregate.Max, selector,
            descending: true, thenBy: false);

        Assert.Equal(new[] { "Linus", "Ada", "Grace" }, min.Select(x => x.Name));
        Assert.Equal(new[] { "Ada", "Grace", "Linus" }, max.Select(x => x.Name));
    }

    [Fact]
    public void Relationship_filter_without_related_authorization_fails_before_execution()
    {
        var filter = new SemanticRelationshipFilter(CustomerOrders, SemanticRelationshipQuantifier.Some,
            new SemanticFieldFilter(Sku, SemanticFilterOperator.Eq, "A"));
        var ir = Ir(new SemanticQueryOptions(filter));
        var capabilities = OrmProviderCapabilities.SharedLinq("test-orm") with
        {
            ContractVersion = "3",
            Features = OrmProviderCapabilities.SharedLinq("test-orm").Features | OrmProviderFeature.RelationshipFilters
        };

        Assert.Throws<NotSupportedException>(() => new OrmPlanCompiler("test-orm", Registry(), capabilities).Compile(ir));
    }

    [Fact]
    public async Task Tenant_predicate_isolates_rows()
    {
        var a = await RunAsync(Ir(), Context(TenantA));
        var b = await RunAsync(Ir(), Context(TenantB));

        Assert.Equal(3, a.Rows.Count);
        Assert.Equal(new[] { "Eve" }, b.Rows.Select(r => (string)r.Values["Name"]!));
    }

    [Fact]
    public async Task User_filter_can_only_narrow_authorization()
    {
        // The caller tries to widen by asking for tenant B's row while authorized as tenant A.
        var options = new SemanticQueryOptions(new SemanticFieldFilter(Tenant, SemanticFilterOperator.Eq, TenantB));
        var result = await RunAsync(Ir(options), Context(TenantA));
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Filters_order_and_in_operator_compose()
    {
        var options = new SemanticQueryOptions(
            new SemanticAndFilter(
            [
                new SemanticFieldFilter(Active, SemanticFilterOperator.Eq, true),
                new SemanticFieldFilter(Name, SemanticFilterOperator.In, new object[] { "Ada", "Grace", "Linus" })
            ]),
            [new SemanticOrderTerm(Name, SemanticSortDirection.Desc)]);

        var result = await RunAsync(Ir(options), Context(TenantA));
        Assert.Equal(new[] { "Grace", "Ada" }, result.Rows.Select(r => (string)r.Values["Name"]!));
    }

    [Fact]
    public async Task Nullable_field_equality_and_or_composition()
    {
        var options = new SemanticQueryOptions(
            new SemanticOrFilter(
            [
                new SemanticFieldFilter(Email, SemanticFilterOperator.Eq, null),
                new SemanticFieldFilter(Name, SemanticFilterOperator.Eq, "Ada")
            ]),
            [new SemanticOrderTerm(Id, SemanticSortDirection.Asc)]);

        var result = await RunAsync(Ir(options), Context(TenantA));
        Assert.Equal(new[] { "Ada", "Linus" }, result.Rows.Select(r => (string)r.Values["Name"]!));
    }

    [Fact]
    public async Task Only_authorized_fields_are_materialized()
    {
        var result = await RunAsync(Ir(fields: [Id, Name]), Context(TenantA));
        var row = result.Rows[0];
        Assert.False(row.Values.ContainsKey("Email"));
        Assert.False(row.Values.ContainsKey("TenantId"));
        Assert.Equal(2, row.EffectiveCells.Count);
    }

    [Fact]
    public async Task Forward_paging_reports_next_page_and_is_deterministic()
    {
        var options = new SemanticQueryOptions(Limit: 2);
        var result = await RunAsync(Ir(options), Context(TenantA));

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.PageInfo!.HasNextPage);
        Assert.Equal(new[] { 1, 2 }, result.Rows.Select(r => (int)r.Values["Id"]!)); // primary-key tiebreak
    }

    [Fact]
    public async Task Offset_paging_skips_rows()
    {
        var options = new SemanticQueryOptions(Limit: 2, Offset: 2);
        var result = await RunAsync(Ir(options), Context(TenantA));
        Assert.Equal(new[] { 3 }, result.Rows.Select(r => (int)r.Values["Id"]!));
        Assert.Null(result.PageInfo);
    }

    [Fact]
    public async Task Missing_context_value_fails_closed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Ir(), new ExecutionContext()));
    }

    [Fact]
    public void Unsupported_shapes_fail_at_compile_time()
    {
        var compiler = new OrmPlanCompiler("test-orm", Registry());

        // Traversal and cursor paging are supported now (see OrmTraversalTests); what stays out of the capability set:
        var noRelationship = new ExecutionIRNode(2, ExecutionOperation.Traverse, Customer, [Id], null, null, []);
        Assert.Throws<InvalidOperationException>(() => compiler.Compile(Ir(children: [noRelationship])));
        Assert.Throws<NotSupportedException>(() =>
            compiler.Compile(Ir(new SemanticQueryOptions(
                Order: [new SemanticOrderTerm(Tenant, SemanticSortDirection.Asc)], Limit: 1, After: "cursor"))));
        Assert.Throws<NotSupportedException>(() =>
            compiler.Compile(Ir(new SemanticQueryOptions(
                new SemanticRelationshipFilter(new Foundgine.Core.Abstractions.RelationshipId(1),
                    SemanticRelationshipQuantifier.Some,
                    new SemanticFieldFilter(Name, SemanticFilterOperator.Eq, "x"))))));
    }

    [Fact]
    public void Compiler_certifies_declared_invariants_and_binds_provenance()
    {
        var registry = Registry();
        var compiler = new OrmPlanCompiler("test-orm", registry);
        var ir = Ir();
        var plan = compiler.Compile(ir);

        Assert.NotNull(plan.AuthorizationBinding);
        compiler.Evaluate(ir, plan).EnsureSatisfied();
    }

    [Fact]
    public void Capability_profile_rejects_unsupported_query_shape_before_execution()
    {
        var capabilities = new OrmProviderCapabilities(
            "test-orm", "1", OrmProviderFeature.Scan | OrmProviderFeature.Projection |
                OrmProviderFeature.AuthorizationPredicates);
        var compiler = new OrmPlanCompiler("test-orm", Registry(), capabilities);
        var compiled = Assert.IsType<OrmPlan>(compiler.Compile(Ir()));
        Assert.Equal("1", compiled.CapabilityContractVersion);
        Assert.Equal(capabilities.Features, compiled.DeclaredFeatures);

        Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(
            new SemanticQueryOptions(Filter: new SemanticFieldFilter(Active, SemanticFilterOperator.Eq, true)))));
        Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(
            new SemanticQueryOptions(Order: [new SemanticOrderTerm(Name, SemanticSortDirection.Asc)]))));
    }

    [Fact]
    public void Capability_profile_must_match_the_provider_identity()
    {
        var capabilities = OrmProviderCapabilities.SharedLinq("efcore");
        Assert.Throws<InvalidOperationException>(() =>
            new OrmPlanCompiler("other-provider", Registry(), capabilities));
    }

    [Fact]
    public void Conformance_rejects_plan_from_another_provider_or_with_different_provenance()
    {
        var compiler = new OrmPlanCompiler("test-orm", Registry());
        var ir = Ir();
        var wrongProviderPlan = new OrmPlan("other-provider", ir);
        Assert.False(compiler.Evaluate(ir, wrongProviderPlan).IsSatisfied);

        var validPlan = compiler.Compile(ir);
        var changedIr = ir with { AuthorizationBinding = ir.AuthorizationBinding with
            { AuthorizationFingerprint = "different-authorization" } };
        Assert.False(compiler.Evaluate(changedIr, validPlan).IsSatisfied);
    }

    [Fact]
    public void Conformance_rejects_unknown_required_security_invariants()
    {
        var compiler = new OrmPlanCompiler("test-orm", Registry());
        var ir = Ir() with { RequiredSecurityInvariants = ["security.invariant.not-registered"] };
        var plan = compiler.Compile(ir);
        var result = compiler.Evaluate(ir, plan);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("not registered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executor_rejects_plan_with_transplanted_authorization_provenance_before_querying()
    {
        var registry = Registry();
        var compiler = new OrmPlanCompiler("test-orm", registry);
        var authorizedIr = Ir();
        var otherIr = Ir(new SemanticQueryOptions(
            Filter: new SemanticFieldFilter(Active, SemanticFilterOperator.Eq, true)));
        var compiled = Assert.IsType<OrmPlan>(compiler.Compile(authorizedIr));
        var transplanted = compiled with { IR = otherIr };
        var source = new CountingSource();
        var executor = new OrmExecutionProvider("test-orm", source, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(transplanted, Context(TenantA)));
        Assert.Equal(0, source.QueryCount);
    }

    [Fact]
    public async Task Executor_rejects_unbound_plan_before_querying()
    {
        var registry = Registry();
        var unbound = new OrmPlan("test-orm", Ir());
        var source = new CountingSource();
        var executor = new OrmExecutionProvider("test-orm", source, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(unbound, Context(TenantA)));
        Assert.Equal(0, source.QueryCount);
    }

    [Fact]
    public void Registry_rejects_metadata_the_generated_map_does_not_cover()
    {
        var drifted = Metadata() with
        {
            Fields = [.. Metadata().EffectiveFields, new Foundgine.Core.Semantic.Metadata.FieldMetadata(
                new Foundgine.Core.Abstractions.FieldId(99), "Ghost", typeof(string))]
        };
        Assert.Throws<InvalidOperationException>(() =>
            OrmRegistry.Create([drifted, .. AllMetadata().Skip(1)], GeneratedOrmMaps.CreateAll()));
    }
}
