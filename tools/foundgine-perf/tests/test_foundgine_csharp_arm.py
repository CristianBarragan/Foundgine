import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ARM = ROOT / "src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi"
# The HTTP endpoint now lives in the library; the benchmark keeps only its rules (BenchmarkGraphQL.cs).
LIB_ENDPOINT = ROOT / "src/csharp/Foundgine.Extensions.GraphQL/Http/GraphQLHttpEndpoint.cs"


def test_foundgine_host_registers_real_query_and_mutation_executors():
    program = (ARM / "Program.cs").read_text()
    endpoint = LIB_ENDPOINT.read_text()
    assert "MapFoundgineGraphQL(BenchmarkGraphQL.Configure)" in program
    assert "options.UseEngineProvider()" in program
    assert "options.MutationSchema = metadata" in program
    assert "AddSingleton<IMutationSchema>(metadata)" in program
    assert "options.MutationProviderFactory" in program
    assert "FoundgineGraphQLQueryExecutor" in program
    assert "FoundgineGraphQLMutationExecutor" in program
    assert "MapPost(options.Path" in endpoint
    assert "http.User.Identity?.IsAuthenticated != true" in endpoint
    assert "TryExecuteAsync" in endpoint


def test_mutation_provider_uses_a_fresh_context_per_atomic_batch():
    source = (ARM / "FactoryEfCoreMutationExecutionProvider.cs").read_text()
    assert "IDbContextFactory<FoundgineDbContext>" in source
    assert "contexts.CreateDbContext()" in source
    assert "EfCoreMutationExecutionProvider" in source
    assert "IMutationSecurityConformanceEvaluator" in source


def test_keycloak_grants_match_the_supplychain_realm_and_operation_contract():
    settings = json.loads((ARM / "appsettings.example.json").read_text())
    runtime_settings = json.loads((ARM / "appsettings.json").read_text())
    grants = settings["Foundgine"]["Keycloak"]["RoleGrants"]
    assert runtime_settings["Foundgine"]["Keycloak"]["RoleGrants"] == grants
    assert set(grants) == {"sc-customer", "sc-operator", "sc-analyst", "sc-manager"}
    assert any(x["Capability"] == "SalesOrder.read" for x in grants["sc-customer"])
    assert any(x["Capability"] == "Shipment.update" for x in grants["sc-operator"])
    assert any(x["Capability"] == "CatalogProduct.read" for x in grants["sc-operator"])
    assert any(x["Capability"] == "Supplier.read" for x in grants["sc-analyst"])
    assert all(x["ResourceScopes"] == [] for role in grants.values() for x in role)
    assert all("REPLACE_WITH" not in x["Capability"] for role in grants.values() for x in role)


def test_customer_order_scope_is_a_bound_semantic_predicate_from_validated_claim():
    endpoint = (ARM / "BenchmarkGraphQL.cs").read_text()
    policy = (ARM / "WarrantSemanticAuthorizationPolicy.cs").read_text()
    program = (ARM / "Program.cs").read_text()
    assert 'principal.FindFirst("customer_id")' in endpoint
    assert "Customers may only create orders for their own customer_id" in endpoint
    assert "rootFields.Count != 1" in endpoint  # prevents an unvalidated batched second mutation
    assert 'AuthorizationPredicate.ResourceParameter("resource"), "CustomerId"' in policy
    assert 'AuthorizationPredicate.ContextParameter("user"), "CustomerId"' in policy
    assert 'values["user.CustomerId"] = customerId' in program
    assert "AddCustomerOrderScope" not in endpoint  # no query-text injection for tenant/owner scoping


def test_foundgine_migrations_have_an_independent_assembly_and_design_time_factory():
    program = (ARM / "Program.cs").read_text()
    factory = (ARM / "FoundgineBenchmarkDesignTimeFactory.cs").read_text()
    script = (ROOT / "tools/foundgine-perf/csharp/generate-foundgine-migration.sh").read_text()
    assert 'MigrationsAssembly("Foundgine.SupplyChain.BenchmarkApi")' in program
    assert "IDesignTimeDbContextFactory<FoundgineDbContext>" in factory
    assert "dotnet ef migrations add InitialSupplyChain" in script
    provenance = (ROOT / "tools/foundgine-perf/csharp/verify-migration-provenance.sh").read_text()
    assert 'FG="$ROOT/src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi"' in provenance


def test_csharp_warrant_tests_cover_tenant_role_and_owner_boundaries():
    tests = ROOT / "src/csharp/tests/Foundgine.GraphQL.HotChocolate.Tests/SupplyChainWarrantAuthorizationTests.cs"
    project = ROOT / "src/csharp/tests/Foundgine.GraphQL.HotChocolate.Tests/Foundgine.GraphQL.HotChocolate.Tests.csproj"
    source = tests.read_text()
    csproj = project.read_text()
    assert "Customer_warrant_is_bound_to_its_issued_tenant" in source
    assert "Analyst_warrant_does_not_inherit_customer_or_operator_capabilities" in source
    assert "Customer_sales_order_read_uses_a_bound_owner_predicate" in source
    assert "Customer_without_customer_id_gets_a_fail_closed_owner_predicate" in source
    assert "Operator_does_not_receive_customer_owner_predicate" in source
    assert "Foundgine.SupplyChain.BenchmarkApi.csproj" in csproj
    assert "Foundgine.Extensions.Keycloak.csproj" in csproj
