using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticApproximateRetrievalTests
{
    [Fact]
    public void Relational_strategy_does_not_invoke_external_retrieval()
    {
        var source = new FakeSource();
        var model = Model();
        var resolver = new EntityResolver(model, source);
        var result = resolver.Retrieve(
            new SemanticRetrievalRequest(
                new EntityId(1),
                new FieldId(2),
                "Bob Smith",
                RetrievalStrategy.Relational));

        Assert.Empty(result);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Fuzzy_retrieval_returns_authorizable_candidates_and_evidence()
    {
        var source = new FakeSource();
        var resolver = new EntityResolver(Model(), source);

        var result = resolver.Retrieve(
            new SemanticRetrievalRequest(
                new EntityId(1),
                new FieldId(2),
                "Bob Smyth",
                RetrievalStrategy.Fuzzy,
                limit: 2));

        Assert.Equal(2, result.Count);
        Assert.Equal("42", result[0].RecordId);
        Assert.Equal(0.97, result[0].Score);
        Assert.Single(result[0].EffectiveEvidence);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void Graph_similarity_is_a_retrieval_capability_not_a_Cypher_dependency()
    {
        var source = new FakeSource();
        var resolver = new EntityResolver(Model(), source);

        var result = resolver.Retrieve(
            new SemanticRetrievalRequest(
                new EntityId(1),
                null,
                "supplier neighborhood",
                RetrievalStrategy.GraphSimilarity,
                sourceEntity: new EntityId(1),
                relationship: new RelationshipId(7),
                referenceIdentity: "42"));

        Assert.Single(result);
        Assert.Equal(
            RetrievalStrategy.GraphSimilarity,
            source.LastRequest!.Strategy);

        Assert.Equal(
            new RelationshipId(7),
            source.LastRequest.Relationship);
    }

    private static SemanticModel Model() =>
        new SemanticModelBuilder()
            .Entity(
                new EntityId(1),
                "Product",
                e => e
                    .Identity(new FieldId(1), "Id")
                    .Field(new FieldId(2), "Name", typeof(string)))
            .Build();

    private sealed class FakeSource : ICandidateSource, IApproximateCandidateSource
    {
        public int Calls { get; private set; }

        public SemanticRetrievalRequest? LastRequest { get; private set; }

        public IReadOnlyList<IdentityCandidate> FindByIdentity(
            EntityId entityType,
            string identityValue) =>
            [];

        public IReadOnlyList<IdentityCandidate> FindByRelationship(
            RelationshipId relationshipId,
            string sourceIdentityValue) =>
            [];

        public IReadOnlyList<RetrievalCandidate> Retrieve(
            SemanticRetrievalRequest request)
        {
            Calls++;
            LastRequest = request;

            return
            [
                new RetrievalCandidate(
                    new EntityId(1),
                    "42",
                    0.97,
                    new FieldId(2),
                    "42",
                    [
                        new ResolutionEvidence(
                            "matched Product.Name by approximate retrieval")
                    ]),

                new RetrievalCandidate(
                    new EntityId(1),
                    "7",
                    0.91,
                    new FieldId(2),
                    "7")
            ];
        }
    }
}