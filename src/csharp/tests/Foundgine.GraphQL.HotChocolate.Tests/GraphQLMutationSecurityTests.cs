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

public sealed class GraphQLMutationSecurityTests
{
    [Fact]
    public async Task Executor_fails_closed_without_host_security_context()
    {
        var executor = new FoundgineHotChocolateMutationExecutor(
            new RecordingMutations(),
            new HotChocolateMutationAdapter(BuildModel(), BuildMetadata()),
            BuildSchema(),
            new FixedProvider(null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync("""
            mutation {
              createCustomer(input: { name: "attacker" }) { id }
            }
            """));
    }

    [Fact]
    public async Task Executor_uses_host_context_and_never_accepts_graphql_identity()
    {
        var mutations = new RecordingMutations();
        var context = CreateContext("trusted-subject", "trusted-tenant");
        var executor = new FoundgineHotChocolateMutationExecutor(
            mutations,
            new HotChocolateMutationAdapter(BuildModel(), BuildMetadata()),
            BuildSchema(),
            new FixedProvider(context));

        await Assert.ThrowsAsync<NotImplementedException>(() => executor.ExecuteAsync("""
            mutation {
              createCustomer(input: { name: "attacker" }) { id }
            }
            """));

        Assert.Same(context, mutations.LastRequest?.Security);
        Assert.Equal("trusted-subject", mutations.LastRequest?.Security?.Subject);
        Assert.Equal("trusted-tenant", mutations.LastRequest?.Security?.Tenant);
    }

    [Fact]
    public void Converter_preserves_nested_relationship_as_a_semantic_dependency()
    {
        var model = BuildModel();
        var metadata = BuildMetadata();
        var adapter = new HotChocolateMutationAdapter(model, metadata);
        var intent = adapter.Adapt("""
                                   mutation {
                                     createCustomer(input: { name: "parent", orders: [{ total: 10 }] }) { id }
                                   }
                                   """);

        var graph = GraphQLMutationSemanticConverter.ToSemanticGraph([intent], BuildSchema());

        Assert.Equal(2, graph.Operations.Count);
        var dependency = Assert.Single(graph.Operations[1].Dependencies);
        Assert.Equal(0, dependency.SourceOperationIndex);
        Assert.Equal(1, dependency.TargetOperationIndex);
        Assert.Equal(new FieldId(1), dependency.SourceField);
        Assert.Equal(new FieldId(5), dependency.TargetField);
        Assert.Equal(new RelationshipId(1), dependency.Relationship);
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

    private sealed class RecordingMutations : IFoundgineMutations
    {
        public SemanticMutationRequest? LastRequest { get; private set; }

        public MutationDryRunResult DryRun(SemanticMutationRequest request) => throw new NotImplementedException();

        public Task<MutationExecutionResult> ExecuteAsync(
            SemanticMutationRequest request,
            ExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            throw new NotImplementedException();
        }

        public MutationPlanApproval Approve(SemanticMutationRequest request, string approvedBy) =>
            throw new NotImplementedException();

        public Task<MutationExecutionResult> ExecuteApprovedAsync(MutationPlanApproval approval,
            ExecutionContext? context = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
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