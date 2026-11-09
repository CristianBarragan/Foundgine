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

public sealed class FoundgineGraphQLQueryExecutorTests
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

    private sealed class RecordingExecutor : Foundgine.Runtime.IFoundgineExecutor
    {
        public int Calls { get; private set; }
        public SemanticRequest? ReceivedRequest { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public ExecutionResult ResultToReturn { get; set; } = new([]);
        public Exception? ToThrow { get; set; }

        public Task<ExecutionResult> ExecuteAsync(SemanticRequest request, ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ReceivedRequest = request;
            ReceivedToken = cancellationToken;
            if (ToThrow is not null) throw ToThrow;
            return Task.FromResult(ResultToReturn);
        }

        public Task<ExecutionResult> ExecuteAsync(Foundgine.Core.Semantic.Intent.ReadIntent intent,
            ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class SequenceProvider(params SecurityExecutionContext?[] contexts) : ISecurityExecutionContextProvider
    {
        private int _i;
        public int Reads => _i;
        public SecurityExecutionContext? GetSecurityExecutionContext() => contexts[_i++];
    }

    private sealed class ThrowingProvider : ISecurityExecutionContextProvider
    {
        public SecurityExecutionContext? GetSecurityExecutionContext() =>
            throw new InvalidOperationException("secret host internals");
    }

    private static FoundgineGraphQLQueryExecutor Create(
        RecordingExecutor executor, ISecurityExecutionContextProvider provider) =>
        new(executor, new HotChocolateSemanticAdapter(BuildModel()), provider);

    private const string Query = "query { customer { id name } }";

    // ---- missing security context ----

    [Fact]
    public async Task ExecuteAsync_throws_and_never_executes_without_security_context()
    {
        var exec = new RecordingExecutor();
        var sut = Create(exec, new FixedProvider(null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ExecuteAsync(Query));
        Assert.Equal(0, exec.Calls);
    }

    [Fact]
    public async Task Missing_context_is_reported_before_invalid_graphql_is_examined()
    {
        var exec = new RecordingExecutor();
        var sut = Create(exec, new FixedProvider(null));

        var result = await sut.TryExecuteAsync("query { nonExistentField");

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.Equal(0, exec.Calls);
    }

    [Fact]
    public async Task TryExecuteAsync_maps_missing_context_to_unauthenticated()
    {
        var sut = Create(new RecordingExecutor(), new FixedProvider(null));

        var result = await sut.TryExecuteAsync(Query);

        Assert.False(result.IsSuccess);
        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Category);
    }

    [Fact]
    public async Task Context_with_blank_subject_is_rejected()
    {
        var exec = new RecordingExecutor();
        var sut = Create(exec, new FixedProvider(CreateContext() with { Subject = " " }));

        var result = await sut.TryExecuteAsync(Query);

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.Equal(0, exec.Calls);
    }

    [Fact]
    public async Task Provider_fault_fails_closed_without_leaking_internals()
    {
        var exec = new RecordingExecutor();
        var sut = Create(exec, new ThrowingProvider());

        var result = await sut.TryExecuteAsync(Query);

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.DoesNotContain("secret host internals", result.Error!.Message);
        Assert.Equal(0, exec.Calls);
    }

    // ---- authorization isolation ----

    [Fact]
    public async Task Each_call_uses_its_own_freshly_read_security_context()
    {
        var exec = new RecordingExecutor();
        var a = CreateContext("alice", "tenant-a");
        var b = CreateContext("bob", "tenant-b");
        var provider = new SequenceProvider(a, b);
        var sut = Create(exec, provider);

        await sut.ExecuteAsync(Query);
        Assert.Same(a, exec.ReceivedRequest!.Security);

        await sut.ExecuteAsync(Query);
        Assert.Same(b, exec.ReceivedRequest!.Security);
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public async Task Failed_call_does_not_leak_context_into_the_next_call()
    {
        var exec = new RecordingExecutor();
        var provider = new SequenceProvider(CreateContext("alice"), null);
        var sut = Create(exec, provider);

        await sut.ExecuteAsync(Query);
        var second = await sut.TryExecuteAsync(Query);

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, second.Error!.Code);
        Assert.Equal(1, exec.Calls);
    }

    private sealed class AmbientProvider : ISecurityExecutionContextProvider
    {
        public static readonly AsyncLocal<SecurityExecutionContext?> Current = new();
        public SecurityExecutionContext? GetSecurityExecutionContext() => Current.Value;
    }

    private sealed class SubjectEchoExecutor : Foundgine.Runtime.IFoundgineExecutor
    {
        public System.Collections.Concurrent.ConcurrentBag<(string Expected, string? Actual)> Seen { get; } = new();

        public async Task<ExecutionResult> ExecuteAsync(SemanticRequest request, ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Seen.Add((AmbientProvider.Current.Value!.Subject, request.Security?.Subject));
            return new ExecutionResult([]);
        }

        public Task<ExecutionResult> ExecuteAsync(Foundgine.Core.Semantic.Intent.ReadIntent intent,
            ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Concurrent_callers_sharing_one_executor_keep_their_own_contexts()
    {
        var exec = new SubjectEchoExecutor();
        var sut = new FoundgineGraphQLQueryExecutor(
            exec, new HotChocolateSemanticAdapter(BuildModel()), new AmbientProvider());

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            AmbientProvider.Current.Value = CreateContext($"user-{i}");
            await sut.ExecuteAsync(Query);
        })));

        Assert.Equal(50, exec.Seen.Count);
        Assert.All(exec.Seen, x => Assert.Equal(x.Expected, x.Actual));
        Assert.Equal(50, exec.Seen.Select(x => x.Actual).Distinct().Count());
    }

    // ---- execution failures ----

    [Fact]
    public async Task Authorization_denial_maps_to_forbidden()
    {
        var exec = new RecordingExecutor { ToThrow = new SemanticAuthorizationException("denied: Customer.read") };
        var sut = Create(exec, new FixedProvider(CreateContext()));

        var result = await sut.TryExecuteAsync(Query);

        Assert.Equal(GraphQLAdapterErrorCode.Forbidden, result.Error!.Code);
        Assert.DoesNotContain("Customer.read", result.Error!.Message);
    }

    [Fact]
    public async Task Provider_failure_maps_to_generic_execution_failed()
    {
        var exec = new RecordingExecutor { ToThrow = new InvalidOperationException("connection string xyz") };
        var sut = Create(exec, new FixedProvider(CreateContext()));

        var result = await sut.TryExecuteAsync(Query);

        Assert.Equal(GraphQLAdapterErrorCode.ExecutionFailed, result.Error!.Code);
        Assert.DoesNotContain("xyz", result.Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_lets_execution_exceptions_propagate()
    {
        var exec = new RecordingExecutor { ToThrow = new InvalidOperationException("boom") };
        var sut = Create(exec, new FixedProvider(CreateContext()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(Query));
    }

    [Fact]
    public async Task Bad_graphql_is_a_translation_error_and_does_not_execute()
    {
        var exec = new RecordingExecutor();
        var sut = Create(exec, new FixedProvider(CreateContext()));

        var result = await sut.TryExecuteAsync("query { nonExistentField }");

        Assert.False(result.IsSuccess);
        Assert.NotEqual(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.NotEqual(GraphQLAdapterErrorCode.ExecutionFailed, result.Error!.Code);
        Assert.Equal(0, exec.Calls);
    }

    // ---- cancellation ----

    [Fact]
    public async Task Cancellation_token_reaches_the_executor()
    {
        using var cts = new CancellationTokenSource();
        var exec = new RecordingExecutor();
        var sut = Create(exec, new FixedProvider(CreateContext()));

        await sut.ExecuteAsync(Query, cancellationToken: cts.Token);

        Assert.Equal(cts.Token, exec.ReceivedToken);
    }

    [Fact]
    public async Task Already_cancelled_token_throws_without_reading_context_or_executing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var exec = new RecordingExecutor();
        var provider = new SequenceProvider(CreateContext());
        var sut = Create(exec, provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.TryExecuteAsync(Query, cancellationToken: cts.Token));
        Assert.Equal(0, exec.Calls);
        Assert.Equal(0, provider.Reads);
    }

    [Fact]
    public async Task Cancellation_during_execution_is_not_converted_to_an_error()
    {
        var exec = new RecordingExecutor { ToThrow = new OperationCanceledException() };
        var sut = Create(exec, new FixedProvider(CreateContext()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.TryExecuteAsync(Query));
    }

    // ---- success ----

    [Fact]
    public async Task ExecuteAsync_attaches_host_context_and_returns_result_with_shape()
    {
        var expected = new ExecutionResult([new ExecutionRow(new Dictionary<string, object?> { ["id"] = 1 })]);
        var exec = new RecordingExecutor { ResultToReturn = expected };
        var context = CreateContext("trusted", "trusted-tenant");
        var sut = Create(exec, new FixedProvider(context));

        var result = await sut.ExecuteAsync(Query);

        Assert.Same(context, exec.ReceivedRequest!.Security);
        Assert.Same(expected, result.Execution);
        Assert.Contains(result.ResultShape.Fields, f => f.GraphQLName == "id");
        Assert.Contains(result.ResultShape.Fields, f => f.GraphQLName == "name");
    }

    [Fact]
    public async Task TryExecuteAsync_succeeds_with_valid_context_and_query()
    {
        var sut = Create(new RecordingExecutor(), new FixedProvider(CreateContext()));

        var result = await sut.TryExecuteAsync(Query);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var adapter = new HotChocolateSemanticAdapter(BuildModel());
        Assert.Throws<ArgumentNullException>(() =>
            new FoundgineGraphQLQueryExecutor(null!, adapter, new FixedProvider(null)));
        Assert.Throws<ArgumentNullException>(() =>
            new FoundgineGraphQLQueryExecutor(new RecordingExecutor(), null!, new FixedProvider(null)));
        Assert.Throws<ArgumentNullException>(() =>
            new FoundgineGraphQLQueryExecutor(new RecordingExecutor(), adapter, null!));
    }
}
