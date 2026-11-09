using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Runtime;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Extensions.GraphQL.HotChocolate.Tests;

public sealed class FoundgineGraphQLMutationExecutorTests
{
    private const string Mutation = "mutation { createCustomer(input: { name: \"Ada\" }) { id name } }";

    private sealed class RecordingMutations : IFoundgineMutations
    {
        public int Calls { get; private set; }
        public SemanticMutationRequest? LastRequest { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Exception? ToThrow { get; set; }

        public MutationExecutionResult ResultToReturn { get; set; } = new(
            new MutationBatchResult([
                new MutationResult(1, new Dictionary<FieldId, object?>
                    { [new FieldId(1)] = 42L, [new FieldId(2)] = "Ada" })
            ]), "plan", "result", null, null);

        public MutationDryRunResult DryRun(SemanticMutationRequest request) => throw new NotImplementedException();

        public Task<MutationExecutionResult> ExecuteAsync(
            SemanticMutationRequest request,
            ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            LastToken = cancellationToken;
            if (ToThrow is not null) throw ToThrow;
            return Task.FromResult(ResultToReturn);
        }

        public MutationPlanApproval Approve(SemanticMutationRequest request, string approvedBy) =>
            throw new NotImplementedException();

        public Task<MutationExecutionResult> ExecuteApprovedAsync(MutationPlanApproval approval,
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

    private static FoundgineGraphQLMutationExecutor Create(
        RecordingMutations mutations, ISecurityExecutionContextProvider provider) =>
        new(mutations, new HotChocolateMutationAdapter(BuildModel(), BuildMetadata()), BuildSchema(), provider);

    // ---- missing security context ----

    [Fact]
    public async Task ExecuteAsync_throws_and_never_writes_without_security_context()
    {
        var m = new RecordingMutations();
        var sut = Create(m, new FixedProvider(null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ExecuteAsync(Mutation));
        Assert.Equal(0, m.Calls);
    }

    [Fact]
    public async Task Missing_context_is_reported_before_invalid_graphql_is_examined()
    {
        var m = new RecordingMutations();
        var result = await Create(m, new FixedProvider(null)).TryExecuteAsync("mutation { nope");

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Category);
        Assert.Equal(0, m.Calls);
    }

    [Fact]
    public async Task Context_with_blank_subject_is_rejected()
    {
        var m = new RecordingMutations();
        var result = await Create(m, new FixedProvider(CreateContext("s", "t") with { Subject = " " }))
            .TryExecuteAsync(Mutation);

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.Equal(0, m.Calls);
    }

    [Fact]
    public async Task Provider_fault_fails_closed_without_leaking_internals()
    {
        var m = new RecordingMutations();
        var result = await Create(m, new ThrowingProvider()).TryExecuteAsync(Mutation);

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.DoesNotContain("secret host internals", result.Error!.Message);
        Assert.Equal(0, m.Calls);
    }

    // ---- authorization isolation ----

    [Fact]
    public async Task Each_call_uses_its_own_freshly_read_security_context()
    {
        var m = new RecordingMutations();
        var a = CreateContext("alice", "tenant-a");
        var b = CreateContext("bob", "tenant-b");
        var provider = new SequenceProvider(a, b);
        var sut = Create(m, provider);

        await sut.ExecuteAsync(Mutation);
        Assert.Same(a, m.LastRequest!.Security);
        await sut.ExecuteAsync(Mutation);
        Assert.Same(b, m.LastRequest!.Security);
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public async Task Failed_call_does_not_leak_context_into_the_next_call()
    {
        var m = new RecordingMutations();
        var sut = Create(m, new SequenceProvider(CreateContext("alice", "t"), null));

        await sut.ExecuteAsync(Mutation);
        var second = await sut.TryExecuteAsync(Mutation);

        Assert.Equal(GraphQLAdapterErrorCode.Unauthenticated, second.Error!.Code);
        Assert.Equal(1, m.Calls);
    }

    [Fact]
    public async Task Graphql_supplied_tenant_or_subject_is_never_forwarded_as_identity()
    {
        var m = new RecordingMutations();
        var host = CreateContext("trusted-subject", "trusted-tenant");
        var sut = Create(m, new FixedProvider(host));

        // Unknown input properties must be rejected by translation; either way the host context is the only identity.
        var result = await sut.TryExecuteAsync(
            "mutation { createCustomer(input: { name: \"x\", tenantId: \"evil\", subject: \"evil\" }) { id } }");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, m.Calls);
        await sut.ExecuteAsync(Mutation);
        Assert.Equal("trusted-subject", m.LastRequest!.Security!.Subject);
        Assert.Equal("trusted-tenant", m.LastRequest!.Security!.Tenant);
    }

    // ---- execution failures ----

    [Fact]
    public async Task Authorization_denial_maps_to_forbidden()
    {
        var m = new RecordingMutations { ToThrow = new SemanticAuthorizationException("denied: Customer.write") };
        var result = await Create(m, new FixedProvider(CreateContext("s", "t"))).TryExecuteAsync(Mutation);

        Assert.Equal(GraphQLAdapterErrorCode.Forbidden, result.Error!.Code);
        Assert.DoesNotContain("Customer.write", result.Error!.Message);
    }

    [Fact]
    public async Task Provider_failure_maps_to_generic_execution_failed()
    {
        var m = new RecordingMutations { ToThrow = new InvalidOperationException("connection string xyz") };
        var result = await Create(m, new FixedProvider(CreateContext("s", "t"))).TryExecuteAsync(Mutation);

        Assert.Equal(GraphQLAdapterErrorCode.ExecutionFailed, result.Error!.Code);
        Assert.DoesNotContain("xyz", result.Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_lets_execution_exceptions_propagate()
    {
        var m = new RecordingMutations { ToThrow = new InvalidOperationException("boom") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(m, new FixedProvider(CreateContext("s", "t"))).ExecuteAsync(Mutation));
    }

    [Fact]
    public async Task Bad_graphql_is_a_translation_error_and_never_writes()
    {
        var m = new RecordingMutations();
        var result = await Create(m, new FixedProvider(CreateContext("s", "t")))
            .TryExecuteAsync("mutation { doesNotExist(input: { name: \"x\" }) { id } }");

        Assert.False(result.IsSuccess);
        Assert.NotEqual(GraphQLAdapterErrorCode.Unauthenticated, result.Error!.Code);
        Assert.NotEqual(GraphQLAdapterErrorCode.ExecutionFailed, result.Error!.Code);
        Assert.Equal(0, m.Calls);
    }

    [Fact]
    public async Task Result_that_cannot_be_materialized_is_an_execution_failure()
    {
        var m = new RecordingMutations
        {
            ResultToReturn = new MutationExecutionResult(new MutationBatchResult([]), "p", "r", null, null)
        };
        var result = await Create(m, new FixedProvider(CreateContext("s", "t"))).TryExecuteAsync(Mutation);

        Assert.Equal(GraphQLAdapterErrorCode.ExecutionFailed, result.Error!.Code);
    }

    // ---- cancellation ----

    [Fact]
    public async Task Cancellation_token_reaches_the_mutation_engine()
    {
        using var cts = new CancellationTokenSource();
        var m = new RecordingMutations();

        await Create(m, new FixedProvider(CreateContext("s", "t"))).ExecuteAsync(Mutation, cancellationToken: cts.Token);

        Assert.Equal(cts.Token, m.LastToken);
    }

    [Fact]
    public async Task Already_cancelled_token_throws_without_reading_context_or_writing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var m = new RecordingMutations();
        var provider = new SequenceProvider(CreateContext("s", "t"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(m, provider).TryExecuteAsync(Mutation, cancellationToken: cts.Token));
        Assert.Equal(0, m.Calls);
        Assert.Equal(0, provider.Reads);
    }

    [Fact]
    public async Task Cancellation_during_execution_is_not_converted_to_an_error()
    {
        var m = new RecordingMutations { ToThrow = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(m, new FixedProvider(CreateContext("s", "t"))).TryExecuteAsync(Mutation));
    }

    // ---- success ----

    [Fact]
    public async Task ExecuteAsync_attaches_host_context_and_returns_shaped_data()
    {
        var m = new RecordingMutations();
        var host = CreateContext("trusted", "trusted-tenant");

        var result = await Create(m, new FixedProvider(host)).ExecuteAsync(Mutation);

        Assert.Same(host, m.LastRequest!.Security);
        Assert.Same(m.ResultToReturn, result.Execution);
        var created = Assert.IsType<Dictionary<string, object?>>(result.Data["createCustomer"]);
        Assert.Equal(42L, created["id"]);
        Assert.Equal("Ada", created["name"]);
    }

    [Fact]
    public async Task TryExecuteAsync_succeeds_with_valid_context_and_mutation()
    {
        var result = await Create(new RecordingMutations(), new FixedProvider(CreateContext("s", "t")))
            .TryExecuteAsync(Mutation);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void Constructor_rejects_null_dependencies()
    {
        var adapter = new HotChocolateMutationAdapter(BuildModel(), BuildMetadata());
        var p = new FixedProvider(null);
        Assert.Throws<ArgumentNullException>(() => new FoundgineGraphQLMutationExecutor(null!, adapter, BuildSchema(), p));
        Assert.Throws<ArgumentNullException>(() => new FoundgineGraphQLMutationExecutor(new RecordingMutations(), null!, BuildSchema(), p));
        Assert.Throws<ArgumentNullException>(() => new FoundgineGraphQLMutationExecutor(new RecordingMutations(), adapter, null!, p));
        Assert.Throws<ArgumentNullException>(() => new FoundgineGraphQLMutationExecutor(new RecordingMutations(), adapter, BuildSchema(), null!));
    }

    private static SemanticModel BuildModel() =>
        new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(new RelationshipId(1), "Orders", new EntityId(2), RelationshipCardinality.Many))
            .Entity(new EntityId(2), "Order", e => e
                .Identity(new FieldId(3), "Id")
                .Field(new FieldId(4), "Total", typeof(decimal)))
            .Build();

    private static IMutationSchema BuildSchema() => new TestSchema();

    private static SecurityExecutionContext CreateContext(string subject, string tenant) =>
        new(
            new SecurityWarrant(
                "warrant-1", "issuer", subject, "graphql", [],
                SecurityWarrantConstraints.Unrestricted,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                "nonce-1", "key-1", null, []),
            subject, "graphql", tenant);

    private sealed class FixedProvider(SecurityExecutionContext? context) : ISecurityExecutionContextProvider
    {
        public SecurityExecutionContext? GetSecurityExecutionContext() => context;
    }

    private static MetadataRegistry BuildMetadata()
    {
        var registry = new MetadataRegistry();
        registry.Register(new EntityMetadata(
            new EntityId(1), "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long),
                    new ColumnReference(new EntityId(1), new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(new EntityId(1), new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(1), new ColumnId(1))));
        registry.Register(new EntityMetadata(
            new EntityId(2), "Order",
            [
                new ColumnMetadata(new ColumnId(3), "Id"), new ColumnMetadata(new ColumnId(4), "Total"),
                new ColumnMetadata(new ColumnId(5), "CustomerId")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(3), "Id", typeof(long),
                    new ColumnReference(new EntityId(2), new ColumnId(3))),
                new FieldMetadata(new FieldId(4), "Total", typeof(decimal),
                    new ColumnReference(new EntityId(2), new ColumnId(4))),
                new FieldMetadata(new FieldId(5), "CustomerId", typeof(long),
                    new ColumnReference(new EntityId(2), new ColumnId(5)))
            ],
            PrimaryKey: new ColumnReference(new EntityId(2), new ColumnId(3))));
        registry.Register(new RelationshipMetadata(
            new RelationshipId(1), new EntityId(1), new EntityId(2), "Orders",
            new ColumnReference(new EntityId(1), new ColumnId(1)),
            new ColumnReference(new EntityId(2), new ColumnId(5))));
        return registry;
    }

    private sealed class TestSchema : IMutationSchema
    {
        public MutationEntitySchema GetEntity(EntityId id) => id.Value switch
        {
            1 => new MutationEntitySchema(
                new EntityId(1), "Customer",
                new HashSet<ColumnId> { new ColumnId(1), new ColumnId(2) },
                new Dictionary<FieldId, ColumnId?>
                    { [new FieldId(1)] = new ColumnId(1), [new FieldId(2)] = new ColumnId(2) },
                new ColumnId(1)),
            2 => new MutationEntitySchema(
                new EntityId(2), "Order",
                new HashSet<ColumnId> { new ColumnId(3), new ColumnId(4), new ColumnId(5) },
                new Dictionary<FieldId, ColumnId?>
                {
                    [new FieldId(3)] = new ColumnId(3), [new FieldId(4)] = new ColumnId(4),
                    [new FieldId(5)] = new ColumnId(5)
                },
                new ColumnId(3)),
            _ => throw new KeyNotFoundException()
        };

        public MutationRelationshipSchema GetRelationship(RelationshipId id) =>
            new(id, new EntityId(1), new EntityId(2), "Orders", new ColumnId(1), new ColumnId(5));
    }
}
