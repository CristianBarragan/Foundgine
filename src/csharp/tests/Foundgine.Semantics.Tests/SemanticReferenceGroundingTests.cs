using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticReferenceGroundingTests
{
    [Fact]
    public void Grounding_combines_semantic_shape_with_provider_evidence()
    {
        var model = new SemanticModelBuilder()
            .Entity(new EntityId(1), "Customer", e => e
                .Alias("Client")
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .FieldAlias(new FieldId(2), "Full Name"))
            .Build();

        var source = new FakeSource();
        var grounder = new SemanticReferenceGrounder(model, source);
        var result = grounder.Ground("Bob Smith", RetrievalStrategy.Search);

        Assert.Single(result);
        Assert.Equal("Customer", result[0].Interpretation.Contains("Customer") ? "Customer" : "");
        Assert.Equal("42", result[0].Candidates[0].RecordId);
        Assert.Equal(CandidateEvidenceKind.Bm25, result[0].Candidates[0].EvidenceKind);
    }

    private sealed class FakeSource : IApproximateCandidateSource
    {
        public IReadOnlyList<RetrievalCandidate> Retrieve(SemanticRetrievalRequest request) =>
        [
            new(new EntityId(1), "42", .94, new FieldId(2), "42",
                [new ResolutionEvidence("BM25 matched Customer.Name.", CandidateEvidenceKind.Bm25, .94)],
                CandidateEvidenceKind.Bm25)
        ];
    }
}