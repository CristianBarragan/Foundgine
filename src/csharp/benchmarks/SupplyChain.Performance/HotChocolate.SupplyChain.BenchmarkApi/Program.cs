using System.Security.Claims;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Foundgine.SupplyChain.Domain.Models;
using StackExchange.Redis;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("SupplyChain")
    ?? throw new InvalidOperationException("ConnectionStrings:SupplyChain is required.");
var authority = builder.Configuration["Keycloak:Authority"]
    ?? throw new InvalidOperationException("Keycloak:Authority is required.");
var audience = builder.Configuration["Keycloak:Audience"] ?? "supplychain-api";

var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? "redis:6379,abortConnect=false,syncTimeout=2000,connectTimeout=2000";
var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
var dataSource = NpgsqlDataSource.Create(connection);
BaselineRevocationEpochGuard.Migrate(dataSource);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<BaselineRevocationEpochGuard>();
builder.Services.AddDbContext<SupplyChainDbContext>(o => o.UseNpgsql(connection,
    npgsql => npgsql.MigrationsAssembly(typeof(SupplyChainDbContext).Assembly.GetName().Name)));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = authority;
    o.RequireHttpsMetadata = builder.Configuration.GetValue("Keycloak:RequireHttpsMetadata", true);
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = authority,
        ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        NameClaimType = "preferred_username", RoleClaimType = ClaimTypes.Role
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            var realm = context.Principal.FindFirst("realm_access")?.Value;
            if (!string.IsNullOrWhiteSpace(realm))
            {
                using var json = JsonDocument.Parse(realm);
                if (json.RootElement.TryGetProperty("roles", out var roles))
                    foreach (var role in roles.EnumerateArray())
                        identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString() ?? ""));
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddGraphQLServer().AddAuthorization().AddQueryType<SupplyChainQuery>()
    .AddMutationType<SupplyChainMutation>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/graphql") && context.User.Identity?.IsAuthenticated == true)
    {
        var guard = context.RequestServices.GetRequiredService<BaselineRevocationEpochGuard>();
        var token = context.Request.Headers["Authorization"].ToString();
        var rawToken = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token[7..] : string.Empty;
        var jwtId = context.User.FindFirst("jti")?.Value ?? context.User.FindFirst("sub")?.Value;
        var digest = string.IsNullOrEmpty(rawToken) ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
        try { _ = guard.Validate(jwtId, digest); }
        catch (UnauthorizedAccessException) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        catch (Exception) { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
    }
    await next();
});
// Match the Foundgine arm: a missing, invalid or expired bearer token is an HTTP 401,
// while an authenticated caller without the required role gets a GraphQL authorization error.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/graphql") && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    if (context.Request.Path.StartsWithSegments("/graphql") && string.IsNullOrWhiteSpace(context.User.FindFirst("tenant_id")?.Value))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
if (builder.Configuration.GetValue("Database:MigrateOnStart", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SupplyChainDbContext>();
    await db.Database.MigrateAsync();
}
app.MapGraphQL("/graphql");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (SupplyChainDbContext db, BaselineRevocationEpochGuard guard, CancellationToken ct) =>
{
    if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
    _ = guard.Probe();
    return Results.Ok(new { status = "ready", securityState = "anchored" });
});
app.Run();

public sealed class SupplyChainDbContext(DbContextOptions<SupplyChainDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> Orders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> OrderItems => Set<SalesOrderLine>();
    public DbSet<CatalogProduct> Products => Set<CatalogProduct>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<InventoryPosition> Inventory => Set<InventoryPosition>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<OrderAllocation> OrderAllocations => Set<OrderAllocation>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Customer>(e => { e.ToTable("customers"); e.HasKey(x => x.Id).HasName("customers_pkey"); e.Property(x => x.Id).HasColumnName("customer_id").ValueGeneratedOnAdd(); e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100); e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100); e.Property(x => x.Email).HasColumnName("email").HasMaxLength(320); e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_customers_email"); });
        mb.Entity<Supplier>(e => { e.ToTable("suppliers"); e.HasKey(x => x.Id).HasName("suppliers_pkey"); e.Property(x => x.Id).HasColumnName("supplier_id").ValueGeneratedOnAdd(); e.Property(x => x.Name).HasColumnName("supplier_name").HasMaxLength(100); e.Property(x => x.Email).HasColumnName("email").HasMaxLength(320); });
        mb.Entity<Category>(e => { e.ToTable("categories"); e.HasKey(x => x.Id).HasName("categories_pkey"); e.Property(x => x.Id).HasColumnName("category_id").ValueGeneratedOnAdd(); e.Property(x => x.Name).HasColumnName("category_name").HasMaxLength(100); });
        mb.Entity<Warehouse>(e => { e.ToTable("warehouses"); e.HasKey(x => x.Id).HasName("warehouses_pkey"); e.Property(x => x.Id).HasColumnName("warehouse_id").ValueGeneratedOnAdd(); e.Property(x => x.Name).HasColumnName("warehouse_name").HasMaxLength(100); e.Property(x => x.Location).HasColumnName("location").HasMaxLength(200); });
        mb.Entity<Carrier>(e => { e.ToTable("carriers"); e.HasKey(x => x.Id).HasName("carriers_pkey"); e.Property(x => x.Id).HasColumnName("carrier_id").ValueGeneratedOnAdd(); e.Property(x => x.Name).HasColumnName("carrier_name").HasMaxLength(100); });
        mb.Entity<CatalogProduct>(e => { e.ToTable("products", t => t.HasCheckConstraint("products_unit_price_check", "unit_price >= 0")); e.HasKey(x => x.Id).HasName("products_pkey"); e.Property(x => x.Id).HasColumnName("product_id").ValueGeneratedOnAdd(); e.Property(x => x.Name).HasColumnName("product_name").HasMaxLength(200); e.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64); e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2); e.Property(x => x.SupplierId).HasColumnName("supplier_id"); e.Property(x => x.CategoryId).HasColumnName("category_id"); e.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_products_sku"); e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).HasConstraintName("products_supplier_id_fkey").OnDelete(DeleteBehavior.NoAction); e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).HasConstraintName("products_category_id_fkey").OnDelete(DeleteBehavior.NoAction); });
        mb.Entity<InventoryPosition>(e => { e.ToTable("inventory", t => { t.HasCheckConstraint("inventory_quantity_check", "quantity_on_hand >= 0"); t.HasCheckConstraint("inventory_reorder_check", "reorder_level >= 0"); }); e.HasKey(x => x.Id).HasName("inventory_pkey"); e.Property(x => x.Id).HasColumnName("inventory_id").ValueGeneratedOnAdd(); e.Property(x => x.WarehouseId).HasColumnName("warehouse_id"); e.Property(x => x.ProductId).HasColumnName("product_id"); e.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand"); e.Property(x => x.ReorderLevel).HasColumnName("reorder_level"); e.Property<DateTime>("LastUpdated").HasColumnName("last_updated").HasDefaultValueSql("CURRENT_TIMESTAMP"); e.HasIndex(x => new { x.WarehouseId, x.ProductId }).IsUnique().HasDatabaseName("ux_inventory_warehouse_product"); e.HasIndex(x => x.ProductId).HasDatabaseName("ix_inventory_product"); e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).HasConstraintName("inventory_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction); e.HasOne<CatalogProduct>().WithMany(p => p.InventoryPositions).HasForeignKey(x => x.ProductId).HasConstraintName("inventory_product_id_fkey").OnDelete(DeleteBehavior.NoAction); });
        mb.Entity<SalesOrder>(e => { e.ToTable("orders", t => t.HasCheckConstraint("orders_status_check", "status IN ('Pending','Cancelled','Shipped','Delivered')")); e.HasKey(x => x.Id).HasName("orders_pkey"); e.Property(x => x.Id).HasColumnName("order_id").ValueGeneratedOnAdd(); e.Property(x => x.CustomerId).HasColumnName("customer_id"); e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20); e.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(14, 2); e.HasIndex(x => x.CustomerId).HasDatabaseName("ix_orders_customer"); e.HasOne<Customer>().WithMany(c => c.Orders).HasForeignKey(x => x.CustomerId).HasConstraintName("orders_customer_id_fkey").OnDelete(DeleteBehavior.NoAction); });
        mb.Entity<SalesOrderLine>(e => { e.ToTable("order_items", t => { t.HasCheckConstraint("order_items_quantity_check", "quantity > 0"); t.HasCheckConstraint("order_items_unit_price_check", "unit_price >= 0"); }); e.HasKey(x => x.Id).HasName("order_items_pkey"); e.Property(x => x.Id).HasColumnName("order_item_id").ValueGeneratedOnAdd(); e.Property(x => x.OrderId).HasColumnName("order_id"); e.Property(x => x.ProductId).HasColumnName("product_id"); e.Property(x => x.Quantity).HasColumnName("quantity"); e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2); e.HasIndex(x => x.OrderId).HasDatabaseName("ix_order_items_order"); e.HasOne<SalesOrder>().WithMany(o => o.Lines).HasForeignKey(x => x.OrderId).HasConstraintName("order_items_order_id_fkey").OnDelete(DeleteBehavior.NoAction); e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("order_items_product_id_fkey").OnDelete(DeleteBehavior.NoAction); });
        mb.Entity<Shipment>(e => { e.ToTable("shipments", t => t.HasCheckConstraint("shipments_status_check", "shipping_status IN ('In Transit','Out for Delivery','Delivered','Delayed')")); e.HasKey(x => x.Id).HasName("shipments_pkey"); e.Property(x => x.Id).HasColumnName("shipment_id").ValueGeneratedOnAdd(); e.Property(x => x.OrderId).HasColumnName("order_id"); e.Property(x => x.CarrierId).HasColumnName("carrier_id"); e.Property(x => x.WarehouseId).HasColumnName("warehouse_id"); e.Property(x => x.TrackingNumber).HasColumnName("tracking_number").HasMaxLength(100); e.Property(x => x.Status).HasColumnName("shipping_status").HasMaxLength(30); e.HasIndex(x => x.OrderId).HasDatabaseName("ix_shipments_order"); e.HasOne(x => x.Order).WithMany(o => o.Shipments).HasForeignKey(x => x.OrderId).HasConstraintName("shipments_order_id_fkey").OnDelete(DeleteBehavior.NoAction); e.HasOne(x => x.Carrier).WithMany().HasForeignKey(x => x.CarrierId).HasConstraintName("shipments_carrier_id_fkey").OnDelete(DeleteBehavior.NoAction); e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).HasConstraintName("shipments_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction); });
        mb.Entity<OrderAllocation>(e => { e.ToTable("order_allocations", t => t.HasCheckConstraint("order_allocations_quantity_check", "quantity > 0")); e.HasKey(x => new { x.OrderItemId, x.WarehouseId }).HasName("order_allocations_pkey"); e.Property(x => x.OrderItemId).HasColumnName("order_item_id"); e.Property(x => x.WarehouseId).HasColumnName("warehouse_id"); e.Property(x => x.Quantity).HasColumnName("quantity"); e.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.OrderItemId).HasConstraintName("order_allocations_order_item_id_fkey").OnDelete(DeleteBehavior.NoAction); e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).HasConstraintName("order_allocations_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction); });
        mb.Entity<IdempotencyRecord>(e => { e.ToTable("supply_chain_idempotency"); e.HasKey(x => x.IdempotencyKey).HasName("supply_chain_idempotency_pkey"); e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).ValueGeneratedNever(); e.Property(x => x.ActorId).HasColumnName("actor_id"); e.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(50); e.Property(x => x.OrderId).HasColumnName("order_id"); e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP"); e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("supply_chain_idempotency_order_id_fkey").OnDelete(DeleteBehavior.NoAction); });
    }
}

public sealed class OrderAllocation { public int OrderItemId { get; init; } public int WarehouseId { get; init; } public int Quantity { get; init; } }
public sealed class IdempotencyRecord { public string IdempotencyKey { get; init; } = ""; public int ActorId { get; init; } public string Operation { get; init; } = ""; public int OrderId { get; init; } public DateTime CreatedAt { get; init; } }

public sealed class SupplyChainQuery
{
    [Authorize(Roles = new[] { "sc-customer", "sc-operator", "sc-manager" })]
    public async Task<IReadOnlyList<SalesOrder>> GetSalesOrder(int? first, [Service] IHttpContextAccessor accessor, SupplyChainDbContext db, CancellationToken ct)
    {
        var user = accessor.HttpContext?.User ?? new ClaimsPrincipal();
        var q = db.Orders.AsNoTracking().OrderBy(x => x.Id).AsQueryable();
        if (user.IsInRole("sc-customer"))
        {
            if (!int.TryParse(user.FindFirst("customer_id")?.Value, out var customerId)) throw new GraphQLException("customer_id claim required");
            q = q.Where(x => x.CustomerId == customerId);
        }
        return await q.Include(x => x.Lines).Include(x => x.Shipments).Take(Math.Clamp(first ?? 50, 1, 50)).ToListAsync(ct);
    }
    [Authorize(Roles = new[] { "sc-customer", "sc-operator", "sc-analyst", "sc-manager" })]
    public Task<List<CatalogProduct>> GetCatalogProduct(int? first, SupplyChainDbContext db, CancellationToken ct) => db.Products.AsNoTracking().OrderBy(x => x.Id).Take(Math.Clamp(first ?? 50, 1, 50)).ToListAsync(ct);
    [Authorize(Roles = new[] { "sc-analyst", "sc-manager" })]
    public Task<List<Supplier>> GetSupplier(int? first, SupplyChainDbContext db, CancellationToken ct) => db.Suppliers.AsNoTracking().OrderBy(x => x.Id).Take(Math.Clamp(first ?? 50, 1, 50)).ToListAsync(ct);
    [Authorize(Roles = new[] { "sc-operator", "sc-manager" })]
    public Task<List<Shipment>> GetShipment(int? first, SupplyChainDbContext db, CancellationToken ct) => db.Shipments.AsNoTracking().OrderBy(x => x.Id).Take(Math.Clamp(first ?? 50, 1, 50)).ToListAsync(ct);
}

public sealed record ShipmentInput(string Status);
// `where` is plain equality ({ id: 1 }), the same surface the Foundgine GraphQL adapter exposes.
public sealed record ShipmentWhereInput(int Id);
public sealed record SalesOrderLineInput(int ProductId, int Quantity, decimal UnitPrice);
public sealed record SalesOrderInput(int CustomerId, string Status, decimal TotalAmount, IReadOnlyList<SalesOrderLineInput> Lines);

public sealed class SupplyChainMutation
{
    private const string CreateOrderRequestKey = "supplychain.createSalesOrder.invoked";
    private static readonly string[] AllowedShipmentStatuses = ["In Transit", "Out for Delivery", "Delivered", "Delayed"];

    [Authorize(Roles = new[] { "sc-operator", "sc-manager" })]
    public async Task<Shipment> UpdateShipment(ShipmentInput input, ShipmentWhereInput where, SupplyChainDbContext db, CancellationToken ct)
    {
        if (!AllowedShipmentStatuses.Contains(input.Status))
            throw new GraphQLException($"Shipment status must be one of: {string.Join(", ", AllowedShipmentStatuses)}.");
        var entity = await db.Shipments.FirstOrDefaultAsync(x => x.Id == where.Id, ct)
            ?? throw new GraphQLException($"Shipment {where.Id} was not found.");
        entity.Status = input.Status;
        await db.SaveChangesAsync(ct);
        return entity;
    }
    [Authorize(Roles = new[] { "sc-customer" })]
    public async Task<SalesOrder> CreateSalesOrder(SalesOrderInput input, [Service] IHttpContextAccessor accessor, SupplyChainDbContext db, CancellationToken ct)
    {
        var user = accessor.HttpContext?.User ?? new ClaimsPrincipal();
        if (!int.TryParse(user.FindFirst("customer_id")?.Value, out var customerId) || customerId != input.CustomerId)
            throw new GraphQLException("Customers may only create orders for themselves.");
        // Customers may submit only one createSalesOrder operation per HTTP request (matches the Foundgine arm).
        if (accessor.HttpContext is { } http)
        {
            if (http.Items.ContainsKey(CreateOrderRequestKey))
                throw new GraphQLException("Customers may only submit one createSalesOrder operation per request.");
            http.Items[CreateOrderRequestKey] = true;
        }
        // The order and its lines are persisted atomically: a failing line rolls back the order.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entity = new SalesOrder { CustomerId = customerId, Status = input.Status, TotalAmount = input.TotalAmount };
        db.Orders.Add(entity);
        await db.SaveChangesAsync(ct);
        foreach (var line in input.Lines) db.OrderItems.Add(new SalesOrderLine { OrderId = entity.Id, ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = line.UnitPrice });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return entity;
    }
}
