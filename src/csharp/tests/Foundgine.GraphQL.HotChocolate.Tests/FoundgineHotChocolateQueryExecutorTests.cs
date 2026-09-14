using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.GraphQL.HotChocolate;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Xunit;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class FoundgineHotChocolateQueryExecutorTests
{
    private static SemanticModel BuildModel() =>
        new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

    private static SecurityExecutionContext CreateContext(string subject = "subject-1", string tenant = "tenant-a")
    {
        var now = DateTimeOffset.UtcNow;
        var warrant = new SecurityWarrant(
            "warrant-1", "issuer", subject, "graphql",
            [new CapabilityGrant("Customer.read", "read", [])],
            SecurityWarrantConstraints.Unrestricted,
            now.AddMinutes(-1), now.AddMinutes(10), "nonce-1", "issuer-key", null, []);
        return new SecurityExecutionContext(warrant, subject, "graphql", tenant);
    }

    private sealed class FixedProvider(SecurityExecutionContext? context) : ISecurityExecutionContextProvider
    {
        public SecurityExecutionContext? GetSecurityExecutionContext() => context;
    }

    private sealed class RecordingFoundgine : Foundgine.Runtime.IFoundgine
    {
        public SemanticRequest? ReceivedRequest { get; private set; }
        public ExecutionResult ResultToReturn { get; set; } = new([]);

        public SemanticAuthorizationCapabilities DescribeCapabilities() => throw new NotImplementedException();
        public SemanticCapabilityContract DescribeCapabilityContract() => throw new NotImplementedException();

        public SemanticCapabilityContract DescribeCapabilityContract(SecurityExecutionContext security) =>
            throw new NotImplementedException();

        public SemanticVersionSet DescribeVersionSet() => throw new NotImplementedException();
        public Foundgine.Runtime.DryRunResult DryRun(SemanticRequest request) => throw new NotImplementedException();

        public Foundgine.Runtime.PlanApproval ApprovePlan(SemanticRequest request, string approvedBy) =>
            throw new NotImplementedException();

        public Task<ExecutionResult> ExecuteApprovedAsync(Foundgine.Runtime.PlanApproval approval,
            ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExecutionResult> ExecuteAsync(SemanticRequest request, ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedRequest = request;
            return Task.FromResult(ResultToReturn);
        }

        public Task<ExecutionResult> ExecuteAsync(Foundgine.Core.Semantic.Intent.ReadIntent intent,
            ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private const string Query = """
                                 query {
                                   customer {
                                     id
                                     name
                                   }
                                 }
                                 """;

    [Fact]
    public async Task ExecuteAsync_throws_when_no_security_context_is_established()
    {
        var executor = new FoundgineHotChocolateQueryExecutor(
            new RecordingFoundgine(),
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(Query));
    }

    [Fact]
    public async Task ExecuteAsync_attaches_the_host_security_context_to_the_semantic_request()
    {
        var foundgine = new RecordingFoundgine();
        var context = CreateContext();
        var executor = new FoundgineHotChocolateQueryExecutor(
            foundgine,
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(context));

        await executor.ExecuteAsync(Query);

        Assert.NotNull(foundgine.ReceivedRequest);
        Assert.Same(context, foundgine.ReceivedRequest!.Security);
    }

    [Fact]
    public async Task ExecuteAsync_never_lets_graphql_input_substitute_the_host_security_context()
    {
        // The GraphQL adapter has no field through which a query can carry
        // identity/tenant/warrant data; this test asserts the invariant holds
        // even though there is no such field to attack today.
        var foundgine = new RecordingFoundgine();
        var hostContext = CreateContext(subject: "trusted-host-subject", tenant: "trusted-tenant");
        var executor = new FoundgineHotChocolateQueryExecutor(
            foundgine,
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(hostContext));

        await executor.ExecuteAsync(Query);

        Assert.Equal("trusted-host-subject", foundgine.ReceivedRequest!.Security!.Subject);
        Assert.Equal("trusted-tenant", foundgine.ReceivedRequest!.Security!.Tenant);
    }

    [Fact]
    public async Task ExecuteAsync_returns_the_execution_result_and_the_graphql_result_shape()
    {
        var expected = new ExecutionResult([new ExecutionRow(new Dictionary<string, object?> { ["id"] = 1 })]);
        var foundgine = new RecordingFoundgine { ResultToReturn = expected };
        var executor = new FoundgineHotChocolateQueryExecutor(
            foundgine,
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(CreateContext()));

        var result = await executor.ExecuteAsync(Query);

        Assert.Same(expected, result.Execution);
        Assert.NotNull(result.ResultShape);
        Assert.Contains(result.ResultShape.Fields, f => f.GraphQLName == "id");
        Assert.Contains(result.ResultShape.Fields, f => f.GraphQLName == "name");
    }

    [Fact]
    public async Task TryExecuteAsync_maps_missing_security_context_to_an_unauthenticated_error()
    {
        var executor = new FoundgineHotChocolateQueryExecutor(
            new RecordingFoundgine(),
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(null));

        var result = await executor.TryExecuteAsync(Query);

        Assert.False(result.IsSuccess);
        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Category);
    }

    [Fact]
    public async Task TryExecuteAsync_maps_bad_graphql_to_a_non_authentication_error()
    {
        var executor = new FoundgineHotChocolateQueryExecutor(
            new RecordingFoundgine(),
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(CreateContext()));

        var result = await executor.TryExecuteAsync("query { nonExistentField }");

        Assert.False(result.IsSuccess);
        Assert.NotEqual(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
    }

    [Fact]
    public async Task TryExecuteAsync_succeeds_when_context_is_present_and_query_is_valid()
    {
        var executor = new FoundgineHotChocolateQueryExecutor(
            new RecordingFoundgine(),
            new HotChocolateSemanticAdapter(BuildModel()),
            new FixedProvider(CreateContext()));

        var result = await executor.TryExecuteAsync(Query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void Constructor_throws_ArgumentNullException_for_null_securityContextProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new FoundgineHotChocolateQueryExecutor(
            new RecordingFoundgine(),
            new HotChocolateSemanticAdapter(BuildModel()),
            securityContextProvider: null!));
    }
}