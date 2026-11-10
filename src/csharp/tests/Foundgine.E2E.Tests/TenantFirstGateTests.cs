using System.Security.Cryptography;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Mirrors Java TenantFirstGateParityTest. Step 78: the warrant/tenant gate must run before the
/// request is resolved against the semantic model, so denials never carry schema detail.
/// </summary>
public sealed class TenantFirstGateTests
{
    private const string GateMessage = "The warrant does not authorize the requested subject or tenant scope.";
    private const string Probe = "ZzProbeEntity";

    // Text of the resolver's unknown-entity error; present only when resolution ran.
    private const string ResolverMarker = "no semantic descriptor";

    [Fact]
    public void Wrong_tenant_probe_of_unknown_entity_gets_generic_denial()
    {
        using var f = new Fixture();
        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            f.Engine.DryRun(f.UnknownEntityRequest("alice", "tenant-b")));
        Assert.Equal(GateMessage, ex.Message);
        Assert.DoesNotContain(Probe, ex.Message);
        Assert.DoesNotContain(ResolverMarker, ex.Message);
    }

    [Fact]
    public void Missing_tenant_probe_of_unknown_entity_gets_generic_denial()
    {
        using var f = new Fixture();
        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            f.Engine.DryRun(f.UnknownEntityRequest("alice", null)));
        Assert.Equal(GateMessage, ex.Message);
    }

    [Fact]
    public void Wrong_subject_probe_of_unknown_entity_gets_generic_denial()
    {
        using var f = new Fixture();
        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            f.Engine.DryRun(f.UnknownEntityRequest("mallory", "tenant-a")));
        Assert.Equal(GateMessage, ex.Message);
    }

    [Fact]
    public void Unverifiable_warrant_probe_never_reaches_resolver()
    {
        using var f = new Fixture();
        var forged = f.Request(f.Sign("alice", "tenant-a", f.OtherKey), "alice", "tenant-a");
        var ex = Assert.ThrowsAny<Exception>(() => f.Engine.DryRun(forged));
        Assert.NotEqual(GateMessage, ex.Message);
        Assert.DoesNotContain(ResolverMarker, ex.Message);
    }

    [Fact]
    public void Same_tenant_caller_passes_gate_and_reaches_resolver()
    {
        using var f = new Fixture();
        var ex = Assert.ThrowsAny<Exception>(() =>
            f.Engine.DryRun(f.UnknownEntityRequest("alice", "tenant-a")));
        Assert.NotEqual(GateMessage, ex.Message);
        Assert.Contains(ResolverMarker, ex.Message);
    }

    [Fact]
    public async Task Intent_path_is_gated_before_name_resolution()
    {
        using var f = new Fixture();
        var intent = new ReadIntent(
            Probe,
            [],
            Security: f.Context("alice", "tenant-b", f.Sign("alice", "tenant-a", f.Key)));
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Engine.ExecuteAsync(intent));
        Assert.Equal(GateMessage, ex.Message);
    }

    [Fact]
    public async Task Approved_execution_path_is_gated_before_name_resolution()
    {
        using var f = new Fixture();
        var request = f.UnknownEntityRequest("alice", "tenant-b");
        var v = f.Engine.DescribeVersionSet();
        var approval = new PlanApproval(
            request,
            "approval-1",
            "fp",
            v.SemanticModelVersion,
            v.CapabilityContractVersion,
            v.CapabilityVersion,
            v.IntentVersion,
            v.PlanVersion,
            "human@example",
            DateTimeOffset.UtcNow);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Engine.ExecuteApprovedAsync(approval));
        Assert.Equal(GateMessage, ex.Message);
    }

    [Fact]
    public async Task Valid_same_tenant_request_plans_and_executes()
    {
        using var f = new Fixture();
        var request = f.KnownEntityRequest("alice", "tenant-a");
        Assert.NotNull(f.Engine.DryRun(request));
        var result = await f.Engine.ExecuteAsync(request);
        Assert.NotNull(result);
        Assert.Equal(1, f.Provider.ExecutionCount);
    }

    [Fact]
    public async Task Wrong_tenant_caller_on_known_entity_is_denied_before_any_execution()
    {
        using var f = new Fixture();
        var request = f.KnownEntityRequest("alice", "tenant-b");
        var ex = Assert.Throws<UnauthorizedAccessException>(() => f.Engine.DryRun(request));
        Assert.Equal(GateMessage, ex.Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Engine.ExecuteAsync(request));
        Assert.Equal(0, f.Provider.ExecutionCount);
    }

    private sealed class Fixture : IDisposable
    {
        public RSA Key { get; } = RSA.Create(2048);
        public RSA OtherKey { get; } = RSA.Create(2048);
        public FoundgineEngine Engine { get; }
        public CountingProvider Provider { get; } = new();

        public Fixture()
        {
            var options = new FoundgineOptions
            {
                Model = Banking.BankingSemanticModel.Build(),
                AuthorizationPolicy = new AllowAllSemanticAuthorizationPolicy(),
                WarrantKeyResolver = new TestKeyResolver(Key),
                WarrantReplayStore = new MemorySecurityWarrantReplayStore(),
                ExpectedWarrantIssuer = "issuer"
            };
            Engine = new FoundgineEngine(options, new TestProviderPlanCompiler(), Provider);
        }

        public SemanticRequest UnknownEntityRequest(string subject, string? tenant) =>
            Request(Sign("alice", "tenant-a", Key), subject, tenant);

        public SemanticRequest KnownEntityRequest(string subject, string? tenant)
        {
            var grants = Engine.DescribeCapabilityContract().Capabilities
                .Where(c => c.Operation == "read")
                .Select(c => new CapabilityGrant(c.Id, "read"))
                .ToArray();
            var warrant = SignWithGrants("alice", "tenant-a", grants, Key);
            return new SemanticRequest(
                Banking.BankingSemanticModel.Customer,
                [new SemanticSelection(new FieldId(2), null, [])],
                Security: Context(subject, tenant, warrant));
        }

        public SemanticRequest Request(SecurityWarrant warrant, string subject, string? tenant) =>
            new(new EntityId(9_999_999UL), [new SemanticSelection(new FieldId(1), null, [])],
                Security: Context(subject, tenant, warrant));

        public SecurityExecutionContext Context(string subject, string? tenant, SecurityWarrant warrant) =>
            new(warrant, subject, "api", tenant, tenant);

        public SecurityWarrant Sign(string subject, string tenant, RSA signer) =>
            SignWithGrants(subject, tenant,
                [new CapabilityGrant("Customer.read", "read")], signer);

        public SecurityWarrant SignWithGrants(
            string subject, string tenant, IReadOnlyList<CapabilityGrant> grants, RSA signer)
        {
            var now = DateTimeOffset.UtcNow;
            return SecurityWarrantSigner.Sign(new SecurityWarrant(
                "w-1",
                "issuer",
                subject,
                "api",
                grants,
                new SecurityWarrantConstraints(allowedTenants: [tenant]),
                now.AddMinutes(-1),
                now.AddMinutes(10),
                "nonce-1",
                "key-1",
                null,
                []), signer);
        }

        public void Dispose()
        {
            Key.Dispose();
            OtherKey.Dispose();
        }
    }

    private sealed class TestKeyResolver(RSA key) : ISecurityWarrantKeyResolver
    {
        public RSA Resolve(string keyId) => key;
    }

    private sealed class TestProviderPlanCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
        IProviderSecurityConformanceEvaluator
    {
        public IReadOnlyCollection<string> PreservedSecurityInvariants =>
            SecurityInvariantRegistry.AllInvariants.Select(x => x.Id).ToArray();

        public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan) =>
            new(
                plan.Provider,
                ir.RequiredSecurityInvariants,
                ir.RequiredSecurityInvariants.Where(PreservedSecurityInvariants.Contains).ToArray(),
                Array.Empty<string>());

        public ProviderPlan Compile(ExecutionIR ir) => new TestPlan();
    }

    private sealed class CountingProvider : IExecutionProvider
    {
        public int ExecutionCount { get; private set; }

        public Task<ExecutionResult> ExecuteAsync(
            ProviderPlan plan,
            ExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new ExecutionResult(
                [new ExecutionRow(new Dictionary<string, object?> { ["Name"] = "Alice" })],
                Evidence: ExecutionEvidenceFactory.Create("test", "placeholder", [1], 1, 0)));
        }
    }

    private sealed record TestPlan() : ProviderPlan("test");
}
