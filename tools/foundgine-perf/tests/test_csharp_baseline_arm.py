from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ARM = ROOT / "src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi"


def test_baseline_is_an_independent_ef_core_hot_chocolate_project():
    project = (ARM / "HotChocolate.SupplyChain.BenchmarkApi.csproj").read_text()
    assert "HotChocolate.AspNetCore" in project
    assert "Npgsql.EntityFrameworkCore.PostgreSQL" in project
    assert "Microsoft.EntityFrameworkCore.Design" in project
    assert "<ProjectReference" not in project
    assert "PackageReference Include=\"Foundgine." not in project


def test_baseline_uses_real_ef_migration_api_and_no_ensure_created():
    program = (ARM / "Program.cs").read_text()
    assert "Database.MigrateAsync" in program
    assert "EnsureCreated" not in program
    assert "UseNpgsql" in program


def test_baseline_validates_keycloak_and_requires_tenant_claim():
    program = (ARM / "Program.cs").read_text()
    assert "AddJwtBearer" in program
    assert "ValidateIssuer = true" in program
    assert "ValidateAudience = true" in program
    assert 'FindFirst("tenant_id")' in program
    assert "context.Fail" in program


def test_graphql_surface_matches_csharp_scenario_roots_and_mutations():
    program = (ARM / "Program.cs").read_text()
    for field in ("GetSalesOrder", "GetCatalogProduct", "GetSupplier", "GetShipment", "UpdateShipment", "CreateSalesOrder"):
        assert field in program


def test_baseline_model_maps_all_seeded_tables_and_auxiliary_tables():
    program = (ARM / "Program.cs").read_text()
    for table in ("customers", "suppliers", "categories", "warehouses", "carriers", "products", "inventory", "orders", "order_items", "shipments", "order_allocations", "supply_chain_idempotency"):
        assert f'ToTable("{table}"' in program


def test_migration_generator_uses_dotnet_ef_baseline_name():
    script = (ROOT / "tools/foundgine-perf/csharp/generate-baseline-migration.sh").read_text()
    assert "dotnet ef migrations add Baseline" in script
