using System.Data.Common;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.Providers.Orm;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Foundgine.Orm.Tests.OrmTestModel;

namespace Foundgine.Orm.Tests;

public sealed class EfCoreProviderTests
{
    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<OrmCustomer>(e =>
            {
                e.ToTable("OrmCustomers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasMany(x => x.Orders).WithOne().HasForeignKey(x => x.CustomerId);
            });
            b.Entity<OrmOrder>(e =>
            {
                e.ToTable("OrmOrders");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
            });
        }
    }

    [Fact]
    public async Task Executes_generated_expressions_against_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry).Compile(Ir(
            new SemanticQueryOptions(
                new SemanticFieldFilter(Active, SemanticFilterOperator.Eq, true),
                [new SemanticOrderTerm(Name, SemanticSortDirection.Desc)])));

        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, Context(TenantA));

        Assert.Equal(new[] { "Grace", "Ada" }, result.Rows.Select(r => (string)r.Values["Name"]!));
        Assert.Equal("efcore", result.Evidence!.Provider);
        Assert.Empty(db.ChangeTracker.Entries()); // no tracking
    }

    [Fact]
    public async Task Projects_only_selected_columns_in_the_database_query()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(capture)
            .Options;
        await using var db = new TestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        await db.SaveChangesAsync();
        capture.Commands.Clear();

        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry).Compile(Ir(fields: [Id, Name]));
        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, Context(TenantA));

        Assert.Equal(3, result.Rows.Count);
        var select = Assert.Single(capture.Commands).Split("FROM", 2, StringSplitOptions.TrimEntries)[0];
        Assert.Contains("Id", select, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", select, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email", select, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TenantId", select, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Relationship_some_filter_translates_to_authorized_sqlite_exists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection).AddInterceptors(capture).Options;
        await using var db = new TestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        db.AddRange(SeedOrders());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var relationshipAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId, Sku], authorization: relationshipAuthorization);
        var filter = new SemanticRelationshipFilter(CustomerOrders, SemanticRelationshipQuantifier.Some,
            new SemanticFieldFilter(Sku, SemanticFilterOperator.Eq, "A"));
        var ir = Ir(new SemanticQueryOptions(filter), children: [child]);
        var baseline = OrmProviderCapabilities.SharedLinq("efcore");
        var capabilities = baseline with { ContractVersion = "3", Features = baseline.Features | OrmProviderFeature.RelationshipFilters };
        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry, capabilities).Compile(ir);
        var context = new Foundgine.Core.Execution.ExecutionContext(new Dictionary<string, object?>
        {
            ["ctx.TenantId"] = TenantA,
            ["ctx.CustomerId"] = 1
        });

        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, context);

        Assert.Equal(new[] { "Ada" }, result.Rows.Select(row => (string)row.Values["Name"]!).Distinct());
        Assert.Contains(capture.Commands, sql => sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, sql => sql.Contains("CustomerId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Correlated_count_and_max_aggregates_include_child_authorization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).AddInterceptors(capture).Options;
        await using var db = new TestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        db.AddRange(SeedOrders());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var childAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId, Total], authorization: childAuthorization);
        var count = new SemanticAggregateFilter(CustomerOrders, SemanticFilterAggregate.Count, null, SemanticAggregateFilterOperator.Gt, 1);
        var max = new SemanticAggregateFilter(CustomerOrders, SemanticFilterAggregate.Max, Total, SemanticAggregateFilterOperator.Gt, 15m);
        var filter = new SemanticAndFilter([count, max]);
        var ir = Ir(new SemanticQueryOptions(filter), children: [child]);
        var baseline = OrmProviderCapabilities.SharedLinq("efcore");
        var capabilities = baseline with { ContractVersion = "4", Features = baseline.Features | OrmProviderFeature.AggregateFilters };
        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry, capabilities).Compile(ir);
        var context = new Foundgine.Core.Execution.ExecutionContext(new Dictionary<string, object?>
        {
            ["ctx.TenantId"] = TenantA,
            ["ctx.CustomerId"] = 1
        });

        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, context);

        Assert.Equal(new[] { "Ada" }, result.Rows.Select(row => (string)row.Values["Name"]!).Distinct());
        Assert.Contains(capture.Commands, sql => sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, sql => sql.Contains("MAX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, sql => sql.Contains("CustomerId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Correlated_count_order_uses_child_authorization_in_sqlite_order_by()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).AddInterceptors(capture).Options;
        await using var db = new TestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        db.AddRange(SeedOrders());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var childAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId], authorization: childAuthorization);
        var optionsOrder = new SemanticQueryOptions(Order:
            [new SemanticOrderTerm(OrderId, SemanticSortDirection.Desc, [CustomerOrders], SemanticOrderAggregate.Count)]);
        var baseline = OrmProviderCapabilities.SharedLinq("efcore");
        var capabilities = baseline with { ContractVersion = "5", Features = baseline.Features | OrmProviderFeature.AggregateOrdering };
        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry, capabilities).Compile(Ir(optionsOrder, children: [child]));
        var context = new Foundgine.Core.Execution.ExecutionContext(new Dictionary<string, object?>
        {
            ["ctx.TenantId"] = TenantA,
            ["ctx.CustomerId"] = 1
        });

        capture.Commands.Clear(); // drop the seeding INSERTs
        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, context);

        Assert.Equal(new[] { "Ada", "Grace", "Linus" }, result.Rows.Select(row => (string)row.Values["Name"]!).Distinct());
        Assert.Contains(capture.Commands, sql => sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, sql => sql.Contains("CustomerId", StringComparison.OrdinalIgnoreCase));
        // Two SELECTs run (parent page, then the child node); the COUNT-ordered one is the parent query.
        var aggregateOrderSql = Assert.Single(capture.Commands, sql => sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ORDER BY", aggregateOrderSql, StringComparison.OrdinalIgnoreCase);
        // The three parent rows deliberately exercise a tie: Grace and Linus both have
        // zero authorized child rows. The generated primary-key tie-breaker makes that
        // ordering deterministic even though aggregate pagination is still gated.
        Assert.Contains("Id", aggregateOrderSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", aggregateOrderSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", aggregateOrderSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", aggregateOrderSql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("offset")]
    [InlineData("cursor")]
    public void Efcore_sqlite_profile_rejects_count_pagination_shapes_until_conformance_is_certified(string mode)
    {
        var childAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId], authorization: childAuthorization);
        var aggregateOrder = new SemanticOrderTerm(
            OrderId, SemanticSortDirection.Asc, [CustomerOrders], SemanticOrderAggregate.Count);
        var after = OrmCursor.EncodeWithInt32Components(new object?[] { 2, 1 }, [0]);
        var options = mode switch
        {
            "limit" => new SemanticQueryOptions(Order: [aggregateOrder], Limit: 2),
            "offset" => new SemanticQueryOptions(Order: [aggregateOrder], Offset: 1),
            "cursor" => new SemanticQueryOptions(Order: [aggregateOrder], After: after),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var baseline = OrmProviderCapabilities.SharedLinq("efcore");
        var capabilities = baseline with
        {
            ContractVersion = "8",
            Features = baseline.Features | OrmProviderFeature.AggregateOrdering
        };
        var compiler = new EfCorePlanCompiler(OrmExecutionParityTests.Registry(), capabilities);

        var error = Assert.Throws<NotSupportedException>(() => compiler.Compile(Ir(options, children: [child])));

        Assert.Contains("cannot yet be combined with limit, offset, or cursor pagination", error.Message);
    }

    [Fact]
    public async Task Efcore_runtime_pagination_cannot_bypass_count_aggregate_gate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var childAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId], authorization: childAuthorization);
        var aggregateOrder = new SemanticOrderTerm(
            OrderId, SemanticSortDirection.Asc, [CustomerOrders], SemanticOrderAggregate.Count);
        var baseline = OrmProviderCapabilities.SharedLinq("efcore");
        var capabilities = baseline with
        {
            ContractVersion = "9",
            Features = baseline.Features | OrmProviderFeature.AggregateOrdering
        };
        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry, capabilities).Compile(
            Ir(new SemanticQueryOptions(Order: [aggregateOrder]), children: [child]));
        var contextValues = new Dictionary<string, object?>
        {
            ["ctx.TenantId"] = TenantA,
            ["ctx.CustomerId"] = 1,
            [Foundgine.Core.Execution.ExecutionContextKeys.PaginationLimit] = 2
        };

        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            new EfCoreExecutionProvider(db, registry).ExecuteAsync(
                plan, new Foundgine.Core.Execution.ExecutionContext(contextValues)));

        Assert.Contains("aggregate ordering cannot be combined with runtime pagination", error.Message);
    }

    [Theory]
    [InlineData(SemanticOrderAggregate.Min, SemanticSortDirection.Asc, new[] { "Grace", "Linus", "Ada" })]
    [InlineData(SemanticOrderAggregate.Max, SemanticSortDirection.Desc, new[] { "Ada", "Grace", "Linus" })]
    public async Task Correlated_min_max_order_uses_authorized_child_rows_in_sqlite(
        SemanticOrderAggregate aggregate, SemanticSortDirection direction, string[] expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).AddInterceptors(capture).Options;
        await using var db = new TestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        db.AddRange(SeedOrders());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var childAuthorization = Foundgine.Core.Abstractions.AuthorizationPredicate.Equal(
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"),
            Foundgine.Core.Abstractions.AuthorizationPredicate.Member(Foundgine.Core.Abstractions.AuthorizationPredicate.ContextParameter("ctx"), "CustomerId"));
        var child = Child(2, Order, CustomerOrders, [OrderId], authorization: childAuthorization);
        var order = new SemanticOrderTerm(Total, direction, [CustomerOrders], aggregate);
        var queryOptions = new SemanticQueryOptions(Order: [order]);
        var baseline = OrmProviderCapabilities.SharedLinq("efcore");
        var capabilities = baseline with { ContractVersion = "6", Features = baseline.Features | OrmProviderFeature.AggregateOrdering };
        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry, capabilities).Compile(Ir(queryOptions, children: [child]));
        var context = new Foundgine.Core.Execution.ExecutionContext(new Dictionary<string, object?>
        {
            ["ctx.TenantId"] = TenantA,
            ["ctx.CustomerId"] = 1
        });

        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, context);

        // Flat joined rows: a parent repeats once per authorized child. Distinct keeps first-occurrence order.
        Assert.Equal(expected, result.Rows.Select(row => (string)row.Values["Name"]!).Distinct());
        Assert.Contains(capture.Commands, sql => sql.Contains(aggregate.ToString().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, sql => sql.Contains("CustomerId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, sql => sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Joined_transaction_is_rolled_back_when_the_foundgine_batch_aborts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        using var ambient = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO OrmCustomers (Id, TenantId, Name, Email, IsActive) VALUES (900, 'tenant-a', 'Uncommitted', 'u@example.test', 1)");

        using (var joined = new EfCoreMutationSource(db).BeginTransaction())
            joined.Rollback();

        // A caller must not be able to commit a partially flushed Foundgine batch afterwards.
        Assert.ThrowsAny<InvalidOperationException>(() => ambient.Commit());
        Assert.Equal(0, await db.Set<OrmCustomer>().CountAsync());
    }

    [Fact]
    public async Task Tenant_isolation_matches_the_in_memory_semantics()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(Seed());
        await db.SaveChangesAsync();

        var registry = OrmExecutionParityTests.Registry();
        var plan = new EfCorePlanCompiler(registry).Compile(Ir());
        var result = await new EfCoreExecutionProvider(db, registry).ExecuteAsync(plan, Context(TenantB));

        Assert.Equal(new[] { "Eve" }, result.Rows.Select(r => (string)r.Values["Name"]!));
    }
}
