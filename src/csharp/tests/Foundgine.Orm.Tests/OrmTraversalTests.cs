using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Results;
using Foundgine.Generated.Orm;
using Foundgine.Providers.Orm;
using Xunit;
using static Foundgine.Orm.Tests.OrmTestModel;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Orm.Tests;

/// <summary>
/// Traversal (joined row shape, checked against the real ResultMaterializer), keyset cursor paging and
/// mutations, run over an in-memory source so behaviour is pinned down without a database.
/// </summary>
public sealed class OrmTraversalTests
{
    /// <summary>In-memory "database": entities are live objects (like tracked ORM entities); a transaction snapshots them.</summary>
    internal sealed class InMemoryDb(IEnumerable<object> seed) : IOrmQuerySource, IOrmMutationSource
    {
        private Dictionary<Type, List<object>> _store = seed.GroupBy(x => x.GetType()).ToDictionary(g => g.Key, g => g.ToList());
        public int SaveCount { get; private set; }

        public IQueryable<T> Query<T>() where T : class => Rows<T>().AsQueryable();
        public IQueryable<T> TrackedQuery<T>() where T : class => Rows<T>().AsQueryable();
        public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class =>
            Task.FromResult<IReadOnlyList<T>>(query.ToList());

        public List<T> Rows<T>() where T : class =>
            _store.TryGetValue(typeof(T), out var list) ? list.Cast<T>().ToList() : [];

        public void Add<T>(T entity) where T : class
        {
            if (!_store.TryGetValue(typeof(T), out var list)) _store[typeof(T)] = list = [];
            list.Add(entity);
        }

        public void Remove<T>(T entity) where T : class => _store[typeof(T)].Remove(entity);

        public int SaveChanges()
        {
            SaveCount++;
            // simulate database identity generation for int keys left at 0
            foreach (var (type, list) in _store)
            {
                var id = type.GetProperty("Id");
                if (id is null || id.PropertyType != typeof(int)) continue;
                foreach (var e in list.Where(e => (int)id.GetValue(e)! == 0))
                    id.SetValue(e, list.Max(x => (int)id.GetValue(x)!) + 1);
            }

            return 0;
        }

        public IOrmTransaction BeginTransaction() => new Tx(this);

        private sealed class Tx(InMemoryDb db) : IOrmTransaction
        {
            private readonly Dictionary<Type, List<object>> _snapshot = db._store.ToDictionary(
                kv => kv.Key, kv => kv.Value.Select(Clone).ToList());
            private bool _done;

            private static object Clone(object o) =>
                typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(o, null)!;

            public void Commit() => _done = true;
            public void Rollback() { if (!_done) { db._store = _snapshot; _done = true; } }
            public void Dispose() => Rollback();
        }
    }

    private sealed class Catalog(IReadOnlyList<EntityMetadata> e, IReadOnlyList<RelationshipMetadata> r) : IMetadataCatalog
    {
        public IEnumerable<EntityMetadata> Entities => e;
        public IEnumerable<RelationshipMetadata> Relationships => r;
        public EntityMetadata GetEntity(EntityId id) => throw new NotImplementedException();
        public RelationshipMetadata GetRelationship(RelationshipId id) => throw new NotImplementedException();
        public ModelMetadata GetModel(ModelId id) => throw new NotImplementedException();
        public ConnectionMetadata GetConnection(ConnectionId id) => throw new NotImplementedException();
        public ConversionMetadata? FindConversion(Type s, Type t) => null;
        public AuthorizationMetadata GetAuthorization(AuthorizationId id) => throw new NotImplementedException();
    }

    private static OrmRegistry Registry() => OrmExecutionParityTests.Registry();

    private static async Task<ExecutionResult> RunAsync(ExecutionIR ir, Guid tenant, InMemoryDb? db = null)
    {
        var registry = Registry();
        var plan = new OrmPlanCompiler("test-orm", registry).Compile(ir);
        return await new OrmExecutionProvider("test-orm", db ?? new InMemoryDb(SeedAll()), registry)
            .ExecuteAsync(plan, Context(tenant));
    }

    private static SemanticPlanNode PlanNode(ExecutionIRNode n) =>
        new(n.Id, n.Operation, n.EntityId, n.Fields, n.ViaRelationship, n.Children.Select(PlanNode).ToArray());

    private static SemanticResult Materialize(ExecutionIR ir, ExecutionResult result)
    {
        var model = new Catalog(AllMetadata(), Relationships()).FromMetadata().Build();
        return new ResultMaterializer(model).Materialize(new SemanticPlan(PlanNode(ir.Root)), result);
    }

    private static ExecutionIR CustomerTree(SemanticQueryOptions? options = null) => Ir(
        options, fields: [Id, Name],
        children:
        [
            Child(2, Order, CustomerOrders, [OrderId, Sku], [Child(4, Line, OrderLines, [LineId, Quantity])]),
            Child(3, Tag, CustomerTags, [TagId, Label])
        ]);

    // ------------------------------------------------------------ traversal

    [Fact]
    public async Task Joined_rows_are_a_left_join_cross_product_of_sibling_children()
    {
        var result = await RunAsync(CustomerTree(), TenantA);

        // Ada: orders {1 (lines 1,2), 2 (no lines)} = 3 order-line rows, x tags {vip, early} = 6.
        // Grace: order 3 (line 3), no tags = 1. Linus: no orders, tag "lapsed" = 1.
        Assert.Equal(6 + 1 + 1, result.Rows.Count);

        var linus = result.Rows.Single(r => (string)r.Values["Name"]! == "Linus");
        Assert.DoesNotContain(linus.EffectiveCells.Keys, k => k.NodeId is 2 or 4); // no orders: cells absent
        Assert.Equal("lapsed", linus.Values["3.Label"]); // tag 3 has a long CustomerId, parent key is int

        var grace = result.Rows.Single(r => (string)r.Values["Name"]! == "Grace");
        Assert.DoesNotContain(grace.EffectiveCells.Keys, k => k.NodeId == 3); // tags: none, long/int key join
        Assert.Equal("C", grace.Values["2.Sku"]);
        Assert.Equal(7, grace.Values["4.Quantity"]);
    }

    [Fact]
    public void Cursor_codec_validates_mixed_count_components_in_full_tuple_positions()
    {
        var encoded = OrmCursor.EncodeWithInt32Components(["customer-7", 12, "order-99"], [1]);
        var decoded = OrmCursor.DecodeWithInt32Components(encoded, [1]);
        Assert.Equal(["customer-7", "12", "order-99"], decoded);
        Assert.Equal(12, OrmCursor.DecodeInt32(decoded, 1));

        Assert.Throws<InvalidOperationException>(() =>
            OrmCursor.EncodeWithInt32Components(["customer-7", "12"], [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OrmCursor.EncodeWithInt32Components(["customer-7"], [1]));
        Assert.Throws<ArgumentException>(() =>
            OrmCursor.EncodeWithInt32Components(["customer-7", 12], [1, 1]));

        var malformed = OrmCursor.Encode(["customer-7", "012", "order-99"]);
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeWithInt32Components(malformed, [1]));
    }

    [Fact]
    public void Count_aggregate_order_exposes_reusable_correlated_selector()
    {
        var binding = new OrmRelationshipBinding<OrmCustomer, OrmOrder>(
            "Orders", customer => customer.Orders);
        System.Linq.Expressions.Expression<Func<OrmOrder, bool>> authorized =
            order => order.CustomerId == 1;

        var selector = (System.Linq.Expressions.Expression<Func<OrmCustomer, int>>)
            binding.BuildCountAggregateOrderSelector(authorized);
        var evaluate = selector.Compile();
        var customer = new OrmCustomer
        {
            Id = 1,
            Orders = [new OrmOrder { CustomerId = 1 }, new OrmOrder { CustomerId = 2 }, new OrmOrder { CustomerId = 1 }]
        };

        Assert.Equal(2, evaluate(customer));
        Assert.Equal(typeof(int), selector.ReturnType);
    }

    [Fact]
    public void Count_aggregate_cursor_comparison_reuses_authorized_selector_and_direction()
    {
        var binding = new OrmRelationshipBinding<OrmCustomer, OrmOrder>(
            "Orders", customer => customer.Orders);
        System.Linq.Expressions.Expression<Func<OrmOrder, bool>> authorized =
            order => order.CustomerId == 1;
        var customer = new OrmCustomer
        {
            Id = 1,
            Orders = [new OrmOrder { CustomerId = 1 }, new OrmOrder { CustomerId = 2 }, new OrmOrder { CustomerId = 1 }]
        };

        var afterAscending = (System.Linq.Expressions.Expression<Func<OrmCustomer, bool>>)
            binding.BuildCountAggregateCursorComparison(authorized, 1, descending: false, equal: false);
        var afterDescending = (System.Linq.Expressions.Expression<Func<OrmCustomer, bool>>)
            binding.BuildCountAggregateCursorComparison(authorized, 1, descending: true, equal: false);
        var equal = (System.Linq.Expressions.Expression<Func<OrmCustomer, bool>>)
            binding.BuildCountAggregateCursorComparison(authorized, 2, descending: false, equal: true);

        Assert.True(afterAscending.Compile()(customer));
        Assert.False(afterDescending.Compile()(customer));
        Assert.True(equal.Compile()(customer));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            binding.BuildCountAggregateCursorComparison(authorized, -1, descending: false, equal: false));
    }

    [Fact]
    public void Mixed_count_and_field_cursor_components_compose_lexicographically()
    {
        var relationship = new OrmRelationshipBinding<OrmCustomer, OrmOrder>(
            "Orders", customer => customer.Orders);
        System.Linq.Expressions.Expression<Func<OrmOrder, bool>> authorized =
            order => order.CustomerId == 1;
        var countGreater = (System.Linq.Expressions.Expression<Func<OrmCustomer, bool>>)
            relationship.BuildCountAggregateCursorComparison(authorized, 1, descending: false, equal: false);
        var countEqual = (System.Linq.Expressions.Expression<Func<OrmCustomer, bool>>)
            relationship.BuildCountAggregateCursorComparison(authorized, 1, descending: false, equal: true);
        System.Linq.Expressions.Expression<Func<OrmCustomer, bool>> idGreater = customer => customer.Id > 10;

        var lexicographic = ExpressionComposer.Or(
            countGreater,
            ExpressionComposer.And(countEqual, idGreater));
        var afterCount = new OrmCustomer
        {
            Id = 1,
            Orders = [new OrmOrder { CustomerId = 1 }, new OrmOrder { CustomerId = 1 }]
        };
        var afterTieBreak = new OrmCustomer
        {
            Id = 11,
            Orders = [new OrmOrder { CustomerId = 1 }]
        };
        var before = new OrmCustomer
        {
            Id = 9,
            Orders = [new OrmOrder { CustomerId = 1 }]
        };

        Assert.True(lexicographic.Compile()(afterCount));
        Assert.True(lexicographic.Compile()(afterTieBreak));
        Assert.False(lexicographic.Compile()(before));
    }

    [Fact]
    public async Task Rows_materialize_into_the_expected_result_tree()
    {
        var ir = CustomerTree(new SemanticQueryOptions(Order: [new SemanticOrderTerm(Id, SemanticSortDirection.Asc)]));
        var result = Materialize(ir, await RunAsync(ir, TenantA));

        Assert.Equal(new object[] { 1, 2, 3 }, result.Roots.Select(x => x.IdentityValue).ToArray());

        var ada = result.Roots[0];
        var orders = ada.Children[CustomerOrders];
        var tags = ada.Children[CustomerTags];
        Assert.Equal(new object[] { 1, 2 }, orders.Select(x => x.IdentityValue).ToArray());
        Assert.Equal(2, tags.Count);
        Assert.Equal(2, orders[0].Children[OrderLines].Count);
        Assert.Empty(orders[1].Children.GetValueOrDefault(OrderLines) ?? []);

        var linus = result.Roots[2];
        Assert.Empty(linus.Children.GetValueOrDefault(CustomerOrders) ?? []);
        Assert.Equal(new object[] { 3L }, linus.Children[CustomerTags].Select(x => x.IdentityValue).ToArray());

        var grace = result.Roots[1];
        Assert.Empty(grace.Children.GetValueOrDefault(CustomerTags) ?? []);
    }

    [Fact]
    public async Task Children_of_unauthorized_parents_are_never_read()
    {
        // Eve (tenant B) owns order 4; a tenant A query must not see it even though the child has no predicate.
        var ir = CustomerTree();
        var result = await RunAsync(ir, TenantA);
        Assert.DoesNotContain(result.Rows, r => r.Values.TryGetValue("2.Sku", out var sku) && (string?)sku == "D");
    }

    [Fact]
    public async Task Child_filter_authorization_and_order_apply_per_child_node()
    {
        var child = Child(2, Order, CustomerOrders, [OrderId, Sku],
            options: new SemanticQueryOptions(
                new SemanticFieldFilter(Total, SemanticFilterOperator.In, new object[] { 20m, 30m }),
                [new SemanticOrderTerm(OrderId, SemanticSortDirection.Desc)]));
        var ir = Ir(fields: [Id, Name], children: [child]);
        var result = Materialize(ir, await RunAsync(ir, TenantA));

        Assert.Equal(new object[] { 2 }, result.Roots[0].Children[CustomerOrders].Select(x => x.IdentityValue).ToArray());
        Assert.Equal(new object[] { 3 }, result.Roots[1].Children[CustomerOrders].Select(x => x.IdentityValue).ToArray());
    }

    [Fact]
    public async Task Unselected_join_keys_are_read_but_never_emitted()
    {
        // The parent key (Id) and child key (CustomerId) are not selected; they must not appear in any row.
        var ir = Ir(fields: [Name], children: [Child(2, Order, CustomerOrders, [Sku])]);
        var result = await RunAsync(ir, TenantA);
        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, r => Assert.DoesNotContain(r.EffectiveCells.Keys,
            k => k.FieldId == Id || k.FieldId == OrderCustomerId));
    }

    [Fact]
    public async Task Evidence_covers_every_authorized_node_and_the_joined_row_count()
    {
        var ir = Ir(fields: [Name], children: [Child(2, Order, CustomerOrders, [Sku], authorization: TenantPredicateOnOrder())]);
        var result = await RunAsync(ir, TenantA);
        Assert.Equal(result.Rows.Count, result.Evidence!.RowsReturned);
        Assert.Contains(1, result.Evidence.AuthorizedNodeIds);
    }

    private static AuthorizationPredicate TenantPredicateOnOrder() =>
        AuthorizationPredicate.Constant("true");

    [Fact]
    public void Unsupported_traversal_shapes_fail_at_compile_time()
    {
        var compiler = new OrmPlanCompiler("test-orm", Registry());

        var childLimit = Ir(children: [Child(2, Order, CustomerOrders, [OrderId], options: new SemanticQueryOptions(Limit: 1))]);
        Assert.Throws<NotSupportedException>(() => compiler.Compile(childLimit));

        var unknown = Ir(children: [Child(2, Order, new RelationshipId(999), [OrderId])]);
        Assert.Throws<NotSupportedException>(() => compiler.Compile(unknown));

        var wrongOp = Ir(children: [new ExecutionIRNode(2, ExecutionOperation.Scan, Order, [OrderId], CustomerOrders, null, [])]);
        Assert.Throws<NotSupportedException>(() => compiler.Compile(wrongOp));
    }

    [Fact]
    public async Task Relationships_can_be_traversed_from_the_target_side()
    {
        // Order -> Customer via the same relationship (reverse direction).
        var ir = new ExecutionIR(
            new ExecutionIRNode(1, ExecutionOperation.Scan, Order, [OrderId, Sku], null, null,
                [Child(2, Customer, CustomerOrders, [Name], authorization: TenantPredicate())], null, null),
            [], new SemanticPlanAuthorizationBinding("c", "a"));
        var result = await RunAsync(ir, TenantA);
        Assert.Equal("Ada", result.Rows.First(r => (int)r.Values["Id"]! == 1).Values["2.Name"]);
        Assert.Null(result.Rows.Single(r => (int)r.Values["Id"]! == 4).Values.GetValueOrDefault("2.Name")); // Eve is tenant B
    }

    // ------------------------------------------------------------ cursors

    private static SemanticQueryOptions Page(int limit, string? after = null, SemanticSortDirection dir = SemanticSortDirection.Asc) =>
        new(Order: [new SemanticOrderTerm(Name, dir)], Limit: limit, After: after);

    [Fact]
    public async Task Cursor_pages_walk_the_whole_set_without_gaps_or_repeats()
    {
        var seen = new List<string>();
        string? after = null;
        for (var pageNo = 0; pageNo < 5; pageNo++)
        {
            var result = await RunAsync(Ir(Page(1, after)), TenantA);
            seen.AddRange(result.Rows.Select(r => (string)r.Values["Name"]!));
            Assert.Equal(pageNo > 0, result.PageInfo!.HasPreviousPage);
            if (!result.PageInfo.HasNextPage) break;
            after = result.PageInfo.EndCursor;
            Assert.NotNull(after);
        }

        Assert.Equal(new[] { "Ada", "Grace", "Linus" }, seen);
    }

    [Fact]
    public async Task Descending_cursor_pages_use_the_less_than_branch()
    {
        var first = await RunAsync(Ir(Page(2, null, SemanticSortDirection.Desc)), TenantA);
        Assert.Equal(new[] { "Linus", "Grace" }, first.Rows.Select(r => (string)r.Values["Name"]!));
        Assert.True(first.PageInfo!.HasNextPage);

        var second = await RunAsync(Ir(Page(2, first.PageInfo.EndCursor, SemanticSortDirection.Desc)), TenantA);
        Assert.Equal(new[] { "Ada" }, second.Rows.Select(r => (string)r.Values["Name"]!));
        Assert.False(second.PageInfo!.HasNextPage);
    }

    [Fact]
    public async Task Cursor_ties_are_broken_by_the_primary_key()
    {
        var db = new InMemoryDb(SeedAll().Concat([new OrmCustomer { Id = 9, TenantId = TenantA, Name = "Ada", IsActive = true }]));
        var first = await RunAsync(Ir(Page(1)), TenantA, db);
        var second = await RunAsync(Ir(Page(1, first.PageInfo!.EndCursor)), TenantA, db);
        Assert.Equal(new[] { 1, 9 }, new[] { (int)first.Rows[0].Values["Id"]!, (int)second.Rows[0].Values["Id"]! }
            .Concat(Array.Empty<int>()).Take(2));
    }

    [Fact]
    public async Task Cursor_page_counts_root_rows_not_joined_rows()
    {
        var ir = Ir(Page(1), fields: [Id, Name], children: [Child(2, Order, CustomerOrders, [OrderId])]);
        var result = await RunAsync(ir, TenantA);
        Assert.Equal(2, result.Rows.Count);                       // Ada's two orders, one root
        Assert.True(result.PageInfo!.HasNextPage);

        var next = await RunAsync(Ir(Page(1, result.PageInfo.EndCursor), fields: [Id, Name],
            children: [Child(2, Order, CustomerOrders, [OrderId])]), TenantA);
        Assert.Equal("Grace", (string)next.Rows.Single().Values["Name"]!);
    }

    [Fact]
    public async Task A_forged_cursor_cannot_widen_authorization_or_mismatch_the_ordering()
    {
        // Cursor positions *inside* the authorized set; tenant B's Eve never appears for tenant A.
        var forged = OrmCursor.Encode(["Aaa", 1]);
        var result = await RunAsync(Ir(Page(10, forged)), TenantA);
        Assert.DoesNotContain(result.Rows, r => (string)r.Values["Name"]! == "Eve");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(Ir(Page(1, OrmCursor.Encode(["only-one-value"]))), TenantA));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(Ir(Page(1, "!!not-a-cursor!!")), TenantA));
    }

    [Fact]
    public async Task Cursor_fails_closed_for_fields_without_ordering_or_with_null_positions()
    {
        // Guid has no translatable ordering operator: rejected at compile time.
        var guidOrder = new SemanticQueryOptions(Order: [new SemanticOrderTerm(Tenant, SemanticSortDirection.Asc)], Limit: 1,
            After: OrmCursor.Encode([TenantA.ToString(), 1]));
        Assert.Throws<NotSupportedException>(() => new OrmPlanCompiler("test-orm", Registry()).Compile(Ir(guidOrder)));

        // Linus has a null Email: a cursor positioned on it is refused rather than guessed.
        var emailOrder = new SemanticQueryOptions(Order: [new SemanticOrderTerm(Email, SemanticSortDirection.Asc)], Limit: 1,
            After: OrmCursor.Encode([null, 3]));
        await Assert.ThrowsAsync<NotSupportedException>(() => RunAsync(Ir(emailOrder), TenantA));
    }

    [Fact]
    public void Count_cursor_component_uses_invariant_non_nullable_int32_encoding()
    {
        var encoded = OrmCursor.Encode([OrmCursor.EncodeInt32(2147483000), "42"]);
        var decoded = OrmCursor.Decode(encoded);
        Assert.Equal(2147483000, OrmCursor.DecodeInt32(decoded, 0));
        Assert.Equal(42, OrmCursor.DecodeInt32(decoded, 1));
        Assert.Throws<NotSupportedException>(() => OrmCursor.DecodeInt32([null], 0));
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeInt32(["not-an-int"], 0));
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeInt32([], 0));
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeInt32(["-1"], 0));
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeInt32(["+1"], 0));
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeInt32(["01"], 0));
        Assert.Throws<InvalidOperationException>(() => OrmCursor.DecodeInt32(["2147483648"], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrmCursor.EncodeInt32(-1));
    }

    [Fact]
    public void Cursor_round_trips_typed_values()
    {
        var encoded = OrmCursor.Encode(["x", 12, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), 1.5m, null]);
        var decoded = OrmCursor.Decode(encoded);
        Assert.Equal(5, decoded.Count);
        Assert.Equal("12", decoded[1]);
        Assert.Equal("1.5", decoded[3]);
        Assert.Null(decoded[4]);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), DateTime.Parse(decoded[2]!, null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    // ------------------------------------------------------------ mutations

    private static MutationEntitySchema Schema(EntityId id, string name, params FieldMetadata[] fields) =>
        new(id, name, fields.Select(f => f.Column!.ColumnId).ToHashSet(),
            fields.ToDictionary(f => f.Id, f => (ColumnId?)f.Column!.ColumnId), fields[0].Column!.ColumnId);

    private static readonly MutationEntitySchema CustomerSchema = Schema(Customer,"OrmCustomer", AllMetadata()[0].EffectiveFields.ToArray());
    private static readonly MutationEntitySchema OrderSchema = Schema(Order, "OrmOrder", AllMetadata()[1].EffectiveFields.ToArray());

    private static ColumnId Col(FieldId f) => new(f.Value);

    private static ExecutionMutationIR Batch(MutationOperation[] ops, params MutationDependency[] deps) =>
        ExecutionMutationIR.From(new MutationBatchPlan(ops, deps));

    private static OrmMutationExecutionProvider Mutator(InMemoryDb db) => new("test-orm", db, Registry());

    private static SemanticFieldFilter IdIs(int id) => new(Id, SemanticFilterOperator.Eq, id);

    [Fact]
    public void Create_returns_generated_keys_that_later_operations_reference_in_one_transaction()
    {
        var db = new InMemoryDb([]);
        var ir = Batch(
            [
                new(CustomerSchema, MutationKind.Create,
                    [new(Col(Tenant), TenantA), new(Col(Name), "New"), new(Col(Active), true)], null, null, [Id]),
                new(OrderSchema, MutationKind.Create,
                    [MutationFieldValue.FromPrevious(Col(OrderCustomerId), 0, Id), new(Col(Sku), "Z"), new(Col(Total), 5m),
                     new(Col(PlacedOn), new DateTime(2026, 2, 2))], null, null, [OrderId])
            ],
            new MutationDependency(0, 1, Id, Col(OrderCustomerId)));

        var result = Mutator(db).ExecuteBatch(ir, Context(TenantA));

        Assert.Equal(2, result.TotalAffectedRows);
        var customer = db.Rows<OrmCustomer>().Single();
        Assert.Equal(1, customer.Id);
        Assert.Equal(customer.Id, db.Rows<OrmOrder>().Single().CustomerId);
        Assert.Equal(1, result.Results[1].ReturnedValues![OrderId]);
    }

    [Fact]
    public void A_failing_operation_rolls_the_whole_batch_back()
    {
        var db = new InMemoryDb(SeedAll());
        var ir = Batch(
        [
            new(CustomerSchema, MutationKind.Update, [new(Col(Name), "Changed")], IdIs(1), null, null),
            // second op references a field the first never returned -> fails after op 0 already wrote
            new(OrderSchema, MutationKind.Create,
                [MutationFieldValue.FromPrevious(Col(OrderCustomerId), 0, Email), new(Col(Sku), "Z")], null, null, null)
        ], new MutationDependency(0, 1, Email, Col(OrderCustomerId)));

        Assert.Throws<InvalidOperationException>(() => Mutator(db).ExecuteBatch(ir, Context(TenantA)));
        Assert.Equal("Ada", db.Rows<OrmCustomer>().Single(c => c.Id == 1).Name);
        Assert.Equal(4, db.Rows<OrmOrder>().Count);
    }

    [Fact]
    public void Update_and_delete_touch_only_rows_matching_the_filter()
    {
        var db = new InMemoryDb(SeedAll());
        var update = Mutator(db).ExecuteBatch(Batch([new(CustomerSchema, MutationKind.Update,
            [new(Col(Active), false)], new SemanticFieldFilter(Tenant, SemanticFilterOperator.Eq, TenantA), null, [Id])]), Context(TenantA));
        Assert.Equal(3, update.TotalAffectedRows);
        Assert.True(db.Rows<OrmCustomer>().Single(c => c.Id == 4).IsActive); // tenant B untouched

        var delete = Mutator(db).ExecuteBatch(Batch([new(OrderSchema, MutationKind.Delete, [],
            new SemanticFieldFilter(OrderCustomerId, SemanticFilterOperator.Eq, 1), null, [OrderId])]), Context(TenantA));
        Assert.Equal(2, delete.TotalAffectedRows);
        Assert.Equal(2, db.Rows<OrmOrder>().Count);
    }

    [Fact]
    public void Unfiltered_update_and_delete_are_refused_before_any_write()
    {
        var db = new InMemoryDb(SeedAll());
        foreach (var kind in new[] { MutationKind.Update, MutationKind.Delete })
        {
            var ir = Batch([new(CustomerSchema, kind, kind == MutationKind.Update ? [new(Col(Name), "X")] : [], null, null, null)]);
            Assert.Throws<InvalidOperationException>(() => Mutator(db).ExecuteBatch(ir, Context(TenantA)));
        }

        Assert.Equal(4, db.Rows<OrmCustomer>().Count);
        Assert.Equal(0, db.SaveCount);
    }

    [Fact]
    public void Upsert_inserts_when_absent_and_updates_when_present()
    {
        var db = new InMemoryDb(SeedAll());
        ExecutionMutationIR Upsert(int id, string name) => Batch([new(CustomerSchema, MutationKind.Upsert,
            [new(Col(Id), id), new(Col(Tenant), TenantA), new(Col(Name), name), new(Col(Active), true)], null, [Col(Id)], [Id])]);

        Mutator(db).ExecuteBatch(Upsert(1, "Ada2"), Context(TenantA));
        Mutator(db).ExecuteBatch(Upsert(50, "Fifty"), Context(TenantA));

        Assert.Equal("Ada2", db.Rows<OrmCustomer>().Single(c => c.Id == 1).Name);
        Assert.Equal("Fifty", db.Rows<OrmCustomer>().Single(c => c.Id == 50).Name);
        Assert.Equal(5, db.Rows<OrmCustomer>().Count);
    }

    [Fact]
    public void Conformance_reports_only_what_the_orm_path_establishes()
    {
        var ir = Batch([new(CustomerSchema, MutationKind.Update, [new(Col(Name), "X")], IdIs(1), null, null)]);
        var result = Mutator(new InMemoryDb(SeedAll())).Evaluate(ir);
        Assert.Empty(result.Violations);
        Assert.Contains("mutation.atomic", result.Satisfied);
        Assert.DoesNotContain("authorization.ownership", result.Satisfied);
    }
}
