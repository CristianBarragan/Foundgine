using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Security;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Storage.Sql.Security;
using Foundgine.Providers.Storage.Sql.Query;
using Xunit;

namespace Foundgine.E2E.Tests.Security;

public sealed class SqlSecurityConformanceTests
{
    [Fact]
    public void Context_values_must_not_be_embedded_in_command_text()
    {
        var plan = new SqlPlan(
            "SELECT \"id\" FROM \"customer\" WHERE \"tenant\" = '7'",
            [new SqlColumnBinding("id", new EntityId(1), new FieldId(1), "id", 1)],
            [new SqlParameterBinding("auth0", 7, ContextPath: "tenant.id")]);
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.PlanCacheContextIsolation]);

        var result = SqlSecurityConformance.Verify(ir, plan);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("embedded", StringComparison.Ordinal));
    }

    [Fact]
    public void Mutation_invariants_are_not_silently_inferred_from_query_sql()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.AtomicMutation]);
        var plan = new SqlPlan("SELECT 1", [], []);

        var result = SqlSecurityConformance.Verify(ir, plan);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("provider-specific", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_projection_is_required_for_field_visibility()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.FieldVisibility]);
        var plan = new SqlPlan("SELECT 1", [], []);

        var result = SqlSecurityConformance.Verify(ir, plan);

        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("projection", StringComparison.Ordinal));
    }
}

// Provider-level adversarial cases. These intentionally construct malformed
// plans rather than relying on a trusted compiler so the conformance gate is
// tested as a security boundary.
public sealed class M175ProviderAttackTests
{
    [Fact]
    public void Provider_dropping_authorization_predicate_is_rejected()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.AuthorizationRequired]);
        var result = SqlSecurityConformance.Verify(ir, new SqlPlan("SELECT id FROM customer", [], []));
        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("authorization predicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Provider_changing_parameter_semantics_is_rejected_when_binding_is_missing()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.ParameterizedValues]);
        var result = SqlSecurityConformance.Verify(ir,
            new SqlPlan("SELECT id FROM customer WHERE amount = @p0", [], [new SqlParameterBinding("", 42)]));
        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("parameter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Provider_changing_projection_is_rejected_by_field_visibility()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.FieldVisibility]);
        var result = SqlSecurityConformance.Verify(ir, new SqlPlan("SELECT 1", [], []));
        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("projection", StringComparison.Ordinal));
    }

    [Fact]
    public void Provider_embedded_tenant_in_cached_sql_is_rejected()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, [], null),
            [SecurityInvariantIds.PlanCacheContextIsolation]);
        var plan = new SqlPlan(
            "SELECT id FROM customer WHERE tenant = 'tenant-42'",
            [new SqlColumnBinding("id", new EntityId(1), new FieldId(1), "id", 1)],
            [new SqlParameterBinding("auth0", "tenant-42", ContextPath: "user.TenantId")]);

        var result = SqlSecurityConformance.Verify(ir, plan);
        Assert.False(result.IsSatisfied);
        Assert.Contains(result.Violations, x => x.Contains("embedded", StringComparison.Ordinal));
    }
}