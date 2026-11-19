using Foundgine.Core.Mapping;
using Mapping.Benchmark.Aot;
using Foundgine.Core.Semantic.Configuration;
using Foundgine.Core.Semantic.Metadata;
using Mapping.Benchmark;

if (args.Length == 2 && args[0] == "emit")
{
    File.WriteAllText(args[1], MappingSourceEmitter.Emit(Catalog.Built(), "Mapping.Benchmark.Generated", "ScenarioMappings"));
    Console.WriteLine($"wrote {args[1]}");
    return 0;
}

int pass = 0, fail = 0;
void Check(string name, bool ok, string? detail = null) { if (ok) pass++; else fail++; Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail is null ? "" : "  -> " + detail)}"); }
string Scope(MappingModel m, string d) => string.Join(" ; ", ScopeResolver.Resolve(m, d).Select(f => f.ToString()));
bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }
string Cols(MappingModel m, string d, string table) => string.Join(",", m.Get(d).Tables.First(t => t.Table == table).Columns.Select(c => $"{c.Path}={c.Column}"));

// 1. Simple: convention only
var simple = new MappingModel().Map<Customer>(m => { m.Tenant(x => x.TenantId); m.Computed(x => x.DisplayName); m.Table("Customer", "Id").Convention(); });
Check("Simple/convention", Cols(simple, "Customer", "Customer").Contains("Name=Name") && Cols(simple, "Customer", "Customer").Contains("TenantId=TenantId"), Cols(simple, "Customer", "Customer"));
Check("Simple/scope direct", Scope(simple, "Customer") == "Customer: Customer.TenantId = @tenant_id", Scope(simple, "Customer"));

// 2. Renamed + 3. Flattened + Owned columns + 8. Computed + 9. Legacy
var legacy = new MappingModel().Map<Customer>(m =>
{
    m.Tenant(x => x.TenantId); m.Computed(x => x.DisplayName);
    m.Table("tbl_cust", "cust_id").Convention(n => "cust_" + n.ToLowerInvariant())
        .Column(x => x.Id, "cust_id").Column(x => x.TenantId, "tnt")
        .Column(x => x.Name, "FullName")
        .Column(x => x.Address.Street, "street").Column(x => x.Address.City, "city");
});
var lc = Cols(legacy, "Customer", "tbl_cust");
Check("Renamed Name->FullName", lc.Contains("Name=FullName"), lc);
Check("Flattened/Owned Address -> street,city", lc.Contains("Address.Street=street") && lc.Contains("Address.City=city"), lc);
Check("Computed has no column", !lc.Contains("DisplayName"), lc);
Check("Legacy tenant column resolved", Scope(legacy, "Customer") == "tbl_cust: tbl_cust.tnt = @tenant_id", Scope(legacy, "Customer"));

// 4. Complex aggregate: Orders + OrderLines + Payments, ownership + tenant through parents
Order_Model(out var order);
void Order_Model(out MappingModel model) => model = new MappingModel().Map<Order>(m =>
{
    m.Tenant(x => x.TenantId); m.Owner(x => x.CustomerId);
    m.Table("Orders", "Id").Convention();
    m.Child<OrderLine>(x => x.Lines, "OrderLines", "Id", "OrderId", t => t.Convention());
    m.Child<Payment>(x => x.Payments, "Payments", "Id", "OrderId", t => t.Convention());
});
var lines = Scope(order, "Order");
Check("Aggregate root scoped (tenant+owner)", lines.Contains("Orders.TenantId = @tenant_id") && lines.Contains("Orders.CustomerId = @customer_id"), lines);
Check("OrderLine scoped through Orders", Scope(order, "OrderLine").Contains("OrderLines.OrderId->Orders.Id | OrderLines: Orders.TenantId = @tenant_id"), Scope(order, "OrderLine"));
Check("Payment scoped through Orders", Scope(order, "Payment").Contains("Payments.OrderId->Orders.Id"), Scope(order, "Payment"));

// 11. One domain type -> multiple persistence tables (1:1 profile table), same rules
var multi = new MappingModel().Map<Customer>(m =>
{
    m.Tenant(x => x.TenantId);
    m.Table("tbl_customer", "Id").Column(x => x.Id, "Id").Column(x => x.TenantId, "tnt").Column(x => x.Name, "Name");
    m.Table("tbl_customer_profile", "CustomerId", TableRole.Child).Parent("tbl_customer", "CustomerId", "Id").Column(x => x.Bio, "Bio");
});
var ms = Scope(multi, "Customer");
Check("One domain -> two tables, both scoped", ms.Contains("tbl_customer: tbl_customer.tnt") && ms.Contains("tbl_customer_profile.CustomerId->tbl_customer.Id"), ms);

// 10. Multi-tenant shared table: two domain types, one table
var shared = new MappingModel()
    .Map<Customer>(m => { m.Tenant(x => x.TenantId); m.Table("tbl_customer", "Id").Column(x => x.Id, "Id").Column(x => x.TenantId, "tnt").Column(x => x.Name, "Name"); })
    .Map<CustomerSummary>(m => { m.Tenant(x => x.TenantId); m.Table("tbl_customer", "Id").Column(x => x.Id, "Id").Column(x => x.Name, "Name"); });
Check("Shared persistence: both domain types scope on same column", Scope(shared, "Customer") == Scope(shared, "CustomerSummary"), Scope(shared, "CustomerSummary"));

// 6. Inheritance: TPH and TPT
var tph = new MappingModel()
    .Map<Vehicle>(m => { m.Tenant(x => x.TenantId); m.Table("Vehicles", "Id").Convention(); })
    .Map<Car>(m => { m.Base<Vehicle>(); m.Table("Vehicles", "Id", TableRole.Derived).Column(x => x.Doors, "Doors").Discriminator("Kind", "car"); });
Check("TPH derived scoped on shared table", Scope(tph, "Car") == "Vehicles: Vehicles.TenantId = @tenant_id", Scope(tph, "Car"));
var tpt = new MappingModel()
    .Map<Vehicle>(m => { m.Tenant(x => x.TenantId); m.Table("Vehicles", "Id").Convention(); })
    .Map<Truck>(m => { m.Base<Vehicle>(); m.Table("Trucks", "Id", TableRole.Derived).Parent("Vehicles", "Id", "Id").Column(x => x.PayloadKg, "PayloadKg"); });
Check("TPT derived scoped through base table", Scope(tpt, "Truck") == "Trucks.Id->Vehicles.Id | Trucks: Vehicles.TenantId = @tenant_id", Scope(tpt, "Truck"));

// 7. Many-to-many
var m2m = new MappingModel()
    .Map<Product>(m => { m.Tenant(x => x.TenantId); m.Table("Products", "Id").Convention(); m.ManyToMany<Category>(x => x.Categories, "ProductCategories", "ProductId", "CategoryId"); })
    .Map<Category>(m => { m.Tenant(x => x.TenantId); m.Table("Categories", "Id").Convention(); });
Check("Many-to-many join table scoped via left side", Scope(m2m, "Product").Contains("ProductCategories.ProductId->Products.Id | ProductCategories: Products.TenantId"), Scope(m2m, "Product"));

// Fail closed: unreachable table must not be silently unscoped
var broken = new MappingModel().Map<Order>(m => { m.Tenant(x => x.TenantId); m.Table("Orders", "Id").Column(x => x.Id, "Id"); });
Check("Fail closed when tenant column unmapped", Throws<MappingScopeException>(() => ScopeResolver.Resolve(broken, "Order")));

// ACCEPTANCE: same domain rules, different persistence representation => same rules, different resolution
MappingModel Orders(string orders, string tenantCol, string lines, string fk) => new MappingModel().Map<Order>(m =>
{
    m.Tenant(x => x.TenantId); m.Owner(x => x.CustomerId);   // identical authorization declaration
    m.Table(orders, "Id").Column(x => x.Id, "Id").Column(x => x.TenantId, tenantCol).Column(x => x.CustomerId, "cust_id");
    m.Child<OrderLine>(x => x.Lines, lines, "Id", fk, t => t.Convention());
});
var a = Orders("Orders", "TenantId", "OrderLines", "OrderId");
var b = Orders("tbl_order", "tnt", "tbl_order_line", "ord_id");
Check("Acceptance: line scope follows persistence change with no rule change",
    Scope(a, "OrderLine").Contains("OrderLines.OrderId->Orders.Id") && Scope(b, "OrderLine").Contains("tbl_order_line.ord_id->tbl_order.Id | tbl_order_line: tbl_order.tnt = @tenant_id"), Scope(b, "OrderLine"));

// Runtime vs generated (snapshot) equivalence
foreach (var (name, model, domains) in new[] { ("simple", simple, new[] { "Customer" }), ("legacy", legacy, ["Customer"]), ("order", order, ["Order", "OrderLine", "Payment"]), ("multi", multi, ["Customer"]), ("tpt", tpt, ["Truck"]), ("m2m", m2m, ["Product", "Category"]) })
{
    var json = MappingSnapshot.Serialize(model);
    var back = MappingSnapshot.Deserialize(json);
    var same = MappingSnapshot.Serialize(back) == json && domains.All(d => Scope(model, d) == Scope(back, d));
    Check($"Snapshot equivalence: {name}", same);
}

Check("Root key must be a mapped physical column (fails loudly)", Throws<InvalidOperationException>(() => new MappingModel().Map<Cust>(m => m.Table("t", "Id").Column(x => x.Id, "cust_id"))));
// ---- Bridge: lowering to existing metadata ----
// (a) 1:1 equivalence with the existing FoundgineModel compiler, using separate domain and persistence classes.
var viaCompiler = new CompilerModel().Build().GetEntity(Foundgine.Core.Abstractions.EntityId.Create("Cust"));
var viaMapping = new MetadataRegistry();
MappingBridge.Register(new MappingModel().Map<Cust>(m => m.Table("tbl_cust", "cust_id").Column(x => x.Id, "cust_id").Column(x => x.Name, "full_nm").Column(x => x.Email, "email_addr")), viaMapping);
var bridged = viaMapping.GetEntity(Foundgine.Core.Abstractions.EntityId.Create("Cust"));
string Shape(EntityMetadata e) => string.Join("|", new[] { e.EntityId.Value.ToString(), e.EffectiveStorageName, e.PrimaryKey!.ColumnId.Value.ToString() }
    .Concat(e.EffectiveFields.OrderBy(f => f.Name).Select(f => $"{f.Name}:{f.Id.Value}:{f.ClrType.Name}:{f.Column!.ColumnId.Value}"))
    .Concat(e.Columns.Select(c => $"{c.Id.Value}:{c.EffectiveStorageName}").OrderBy(x => x)));
Check("Bridge == existing compiler for a 1:1 mapping (ids, columns, fields, key)", Shape(viaCompiler) == Shape(bridged), Shape(viaCompiler) == Shape(bridged) ? null : "\n" + Shape(viaCompiler) + "\n" + Shape(bridged));

// (b) multi-table aggregate lowers to separate entities + a relationship keyed through the foreign key
var reg2 = new MetadataRegistry();
MappingBridge.Register(order, reg2);
var orderEntity = reg2.GetEntity(Foundgine.Core.Abstractions.EntityId.Create("Order"));
var lineEntity = reg2.GetEntity(Foundgine.Core.Abstractions.EntityId.Create("OrderLine"));
var rel = reg2.Relationships.Single(r => r.Name == "Lines");
Check("Aggregate lowers: Order->Orders, OrderLine->OrderLines", orderEntity.EffectiveStorageName == "Orders" && lineEntity.EffectiveStorageName == "OrderLines");
Check("Relationship keys: Orders.Id -> OrderLines.OrderId", rel.SourceKey.ColumnId == Foundgine.Core.Abstractions.ColumnId.Create("Orders", "Id") && rel.TargetKey.ColumnId == Foundgine.Core.Abstractions.ColumnId.Create("OrderLines", "OrderId") && rel.IsCollection);

// (c) one domain type over two tables: one EntityMetadata, fields point at columns in either table, layout recovers the table
var reg3 = new MetadataRegistry();
MappingBridge.Register(multi, reg3);
var custEntity = reg3.GetEntity(Foundgine.Core.Abstractions.EntityId.Create("Customer"));
var layout = MappingBridge.TableLayout(multi.Get("Customer"));
var bio = custEntity.EffectiveFields.Single(f => f.Name == "Bio");
Check("One domain -> two tables: Bio field resolves to tbl_customer_profile", layout[bio.Column!.ColumnId].Table == "tbl_customer_profile" && custEntity.EffectiveFields.Single(f => f.Name == "Name").Column is not null, layout[bio.Column!.ColumnId].Table);
Check("Field CLR types survive lowering", custEntity.EffectiveFields.Single(f => f.Name == "Id").ClrType == typeof(int) && custEntity.EffectiveFields.Single(f => f.Name == "Name").ClrType == typeof(string));

// (d) many-to-many lowering + both-sides scoping
var reg4 = new MetadataRegistry();
MappingBridge.Register(m2m, reg4);
var cats = reg4.Relationships.Single(r => r.Name == "Categories");
var link = MappingBridge.ManyToMany(m2m).Single();
Check("M2M relationship keyed Products.Id -> Categories.Id", cats.SourceKey.ColumnId == Foundgine.Core.Abstractions.ColumnId.Create("Products", "Id") && cats.TargetKey.ColumnId == Foundgine.Core.Abstractions.ColumnId.Create("Categories", "Id") && cats.IsCollection);
Check("M2M join table recorded", link is { JoinTable: "ProductCategories", LeftColumn: "ProductId", RightColumn: "CategoryId" } && link.Relationship == cats.Id);
var both = ScopeResolver.ResolveLink(m2m, "Product", "Categories");
Check("M2M link scoped on join table AND target entity", both.Any(f => f.QueriedTable == "ProductCategories" && f.Table == "Products") && both.Any(f => f.QueriedTable == "Categories" && f.Table == "Categories" && f.Column == "TenantId"), string.Join(" ; ", both));
Check("ResolveLink rejects non-M2M path", Throws<MappingScopeException>(() => ScopeResolver.ResolveLink(order, "Order", "Lines")));

// ---- Per-scenario files (used for the configuration-volume measurement) ----
foreach (var (name, build, domain, expect) in new (string, Func<MappingModel>, string, string)[]
{
    ("Simple", Mapping.Benchmark.Scenarios.Simple.Build, "Customer", "Customer: Customer.TenantId"),
    ("Renamed", Mapping.Benchmark.Scenarios.Renamed.Build, "Customer", "Customer: Customer.TenantId"),
    ("Flattened", Mapping.Benchmark.Scenarios.Flattened.Build, "Customer", "Customer: Customer.TenantId"),
    ("Owned", Mapping.Benchmark.Scenarios.Owned.Build, "Address", "CustomerAddress.CustomerId->Customer.Id | CustomerAddress: Customer.TenantId"),
    ("Complex", Mapping.Benchmark.Scenarios.Complex.Build, "Payment", "Payments.OrderId->Orders.Id | Payments: Orders.TenantId"),
    ("InheritanceTph", Mapping.Benchmark.Scenarios.InheritanceTph.Build, "Car", "Vehicles: Vehicles.TenantId"),
    ("InheritanceTpt", Mapping.Benchmark.Scenarios.InheritanceTpt.Build, "Truck", "Trucks.Id->Vehicles.Id | Trucks: Vehicles.TenantId"),
    ("ManyToMany", Mapping.Benchmark.Scenarios.ManyToMany.Build, "Product", "ProductCategories.ProductId->Products.Id | ProductCategories: Products.TenantId"),
    ("Computed", Mapping.Benchmark.Scenarios.Computed.Build, "Customer", "Customer: Customer.TenantId"),
    ("Legacy", Mapping.Benchmark.Scenarios.Legacy.Build, "Customer", "tbl_cust: tbl_cust.tnt"),
    ("MultiTenant", Mapping.Benchmark.Scenarios.MultiTenant.Build, "CustomerSummary", "tbl_customer: tbl_customer.TenantId"),
})
{
    var model = build();
    var resolved = Scope(model, domain);
    var snap = MappingSnapshot.Deserialize(MappingSnapshot.Serialize(model));
    Check($"Scenario file {name}: resolves + snapshot-equivalent", resolved.Contains(expect) && Scope(snap, domain) == resolved, resolved);
}

// ---- FoundgineModel integration (UseMappings) ----
var mixed = new MixedModel(Mapping.Benchmark.Scenarios.Complex.Build(), withCompiler: true).Build();
Check("UseMappings + fluent compiler in one Build(): both styles present",
    mixed.Entities.Any(e => e.Name == "Cust") && mixed.Entities.Any(e => e.Name == "Order") && mixed.Entities.Any(e => e.Name == "OrderLine") && mixed.Relationships.Any(r => r.Name == "Lines"));
var only = new MixedModel(Mapping.Benchmark.Scenarios.ManyToMany.Build(), withCompiler: false).Build();
Check("UseMappings alone builds (no fluent configs required)", only.Entities.Count() == 2 && only.Relationships.Any(r => r.Name == "Categories"));
var clash = new MappingModel().Map<Cust>(m => m.Table("other", "id").Column(x => x.Id, "id"));
string? clashMsg = null; try { new MixedModel(clash, withCompiler: true).Build(); } catch (InvalidOperationException e) { clashMsg = e.Message; }
Check("Build() rejects a domain type mapped by both styles", clashMsg is not null && clashMsg.Contains("Cust"), clashMsg);
var unscoped = new MappingModel().Map<Order>(m => { m.Tenant(x => x.TenantId); m.Table("Orders", "Id").Column(x => x.Id, "Id"); });
string? scopeMsg = null; try { new MixedModel(unscoped, withCompiler: false).Build(); } catch (InvalidOperationException e) { scopeMsg = e.Message; }
Check("Build() fails at startup when a tenant column is unreachable", scopeMsg is not null && scopeMsg.Contains("Order") && scopeMsg.Contains("unscoped"), scopeMsg);
string? dupMsg = null; try { var twice = new MixedModel(Mapping.Benchmark.Scenarios.Simple.Build(), withCompiler: false); twice.Extra = Mapping.Benchmark.Scenarios.Simple.Build(); twice.Build(); } catch (InvalidOperationException e) { dupMsg = e.Message; }
Check("Build() rejects the same domain type mapped twice via UseMappings", dupMsg is not null && dupMsg.Contains("more than once"), dupMsg);
var into = new MetadataRegistry(); new MixedModel(Mapping.Benchmark.Scenarios.Complex.Build(), withCompiler: false).BuildInto(into);
Check("BuildInto carries mapping-derived metadata", into.Entities.Any(e => e.Name == "OrderLine"));

// ---- Generated (emitted C#, compiled) vs runtime (fluent-built) ----
var generated = Mapping.Benchmark.Generated.ScenarioMappings.All;
Check("Generated catalog covers every scenario", generated.Count == Catalog.Runtime.Count && Catalog.Runtime.Keys.All(generated.ContainsKey), $"{generated.Count} of {Catalog.Runtime.Count}");
foreach (var (name, build) in Catalog.Runtime)
{
    if (!generated.TryGetValue(name, out var gen)) { Check($"Generated == runtime: {name}", false, "missing"); continue; }
    var rt = build(); var g = gen();
    var diff = StructuralEquality.Diff(rt, g);
    var domains = rt.Mappings.Select(m => m.Domain).ToList();
    string SafeScope(MappingModel mm, string d) { try { return Scope(mm, d); } catch (MappingScopeException e) { return "EX:" + e.Message; } }
    var sameScope = domains.All(d => SafeScope(rt, d) == SafeScope(g, d));
    Check($"Generated == runtime (structural + scope): {name}", diff is null && sameScope, diff);
}
var emitted = MappingSourceEmitter.Emit(Catalog.Built(), "Mapping.Benchmark.Generated", "ScenarioMappings");
var committed = Path.Combine(AppContext.BaseDirectory, "../../../Generated/ScenarioMappings.g.cs");
Check("Generated file is up to date (regenerate and diff)", File.Exists(committed) && File.ReadAllText(committed).ReplaceLineEndings("\n") == emitted.ReplaceLineEndings("\n"), File.Exists(committed) ? null : "file not found: " + committed);
var hasReflection = emitted.Contains("typeof") || emitted.Contains("System.Reflection") || emitted.Contains("JsonSerializer");
Check("Emitted source has no reflection/JSON/typeof", !hasReflection);

Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

// Existing-compiler fixture (declared after top-level statements)
sealed class CompilerModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        model.Configure<CustSemantics>();
        model.Configure<CustTable>();
        model.Configure<CustMap>();
    }
}
sealed class CustSemantics : FoundgineModelConfiguration<Cust> { public override void Configure(FoundgineModelBuilder<Cust> m) { } }
sealed class CustTable : FoundginePersistenceConfiguration<CustRow>
{
    public override void Configure(FoundginePersistenceBuilder<CustRow> m)
    {
        m.ToTable("tbl_cust").HasKey(x => x.CustId);
        m.Property(x => x.CustId).HasColumnName("cust_id");
        m.Property(x => x.FullName).HasColumnName("full_nm");
        m.Property(x => x.EmailAddr).HasColumnName("email_addr");
    }
}
sealed class CustMap : FoundgineEntityMap<Cust, CustRow>
{
    public override void Configure(FoundgineEntityMapBuilder<Cust, CustRow> map)
    {
        map.Property(x => x.Id).To(x => x.CustId);
        map.Property(x => x.Name).To(x => x.FullName);
        map.Property(x => x.Email).To(x => x.EmailAddr);
    }
}

sealed class MixedModel(MappingModel maps, bool withCompiler) : FoundgineModel
{
    public MappingModel? Extra { get; set; }
    public override void Configure(FoundgineModelBuilder model)
    {
        if (withCompiler) { model.Configure<CustSemantics>(); model.Configure<CustTable>(); model.Configure<CustMap>(); }
        model.UseMappings(maps);
        if (Extra is not null) model.UseMappings(Extra);
    }
}
