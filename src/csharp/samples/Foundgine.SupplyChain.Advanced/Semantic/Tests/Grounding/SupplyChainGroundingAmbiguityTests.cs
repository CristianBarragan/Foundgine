using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Grounding;

/// <summary>
/// Case study for <see>
///     <cref>SemanticLexicalResolver.Ground</cref>
/// </see>
/// against the real
/// generated Supply Chain semantic contract (see docs/GROUNDING-DECISIONS.md).
/// 
/// This is not a retrieval-provider test — no Elasticsearch or pgvector is
/// involved, and the two tests below use a fixed <see cref="FakeLexicalSource"/>
/// instead. The point is narrower: given candidates a retrieval provider could
/// plausibly return for this exact schema, does the resolver correctly tell a
/// materially ambiguous business term apart from two pieces of evidence for the
/// same term?
/// 
/// "Show me our active suppliers" is a realistic operator question, and
/// "active" is genuinely ambiguous against this schema:
/// 
///   - a supplier with an open purchase order right now
///     (PurchaseOrder.Status == Open, reached via Supplier.purchaseOrders); or
///   - a supplier whose certification hasn't lapsed
///     (SupplierCertification.ValidTo, reached via Supplier.certifications).
/// 
/// Both are legitimate, both are structurally valid against the frozen
/// contract, and they are not the same meaning — a supplier can satisfy one
/// and not the other. A resolver that just returns the top-scored candidate
/// would silently pick one interpretation and authorize/execute a query the
/// caller never asked for. <see>
///     <cref>SemanticLexicalResolver.Ground</cref>
/// </see>
/// is
/// designed to catch exactly this and refuse to commit.
/// </summary>
public sealed class SupplyChainGroundingAmbiguityTests
{
    [Fact]
    public void Active_supplier_is_a_material_ambiguity_the_resolver_must_not_silently_resolve()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "active", SemanticLexicalCandidateKind.Value, "PurchaseOrder.Status = Open", .90,
                EntityId: SupplyChainSemanticModel.PurchaseOrder,
                FieldId: SupplyChainSemanticModel.Field("PurchaseOrder", "Status"),
                Value: "Open"),
            new SemanticLexicalCandidate(
                "active", SemanticLexicalCandidateKind.Value, "SupplierCertification.ValidTo >= today", .895,
                EntityId: SupplyChainSemanticModel.Certification,
                FieldId: SupplyChainSemanticModel.Field("SupplierCertification", "ValidTo"),
                Value: "current"));

        var decision = new SemanticLexicalResolver(contract, source).Ground("active");

        // Both readings are legal against the contract, and neither dominates
        // on confidence, so Foundgine must stop rather than pick one.
        Assert.Equal(GroundingOutcome.RequiresClarification, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.Equal(2, decision.CompetingInterpretations.Count);
        Assert.Contains(decision.CompetingInterpretations,
            x => x.Steps[0].Candidate.CanonicalName == "PurchaseOrder.Status = Open");
        Assert.Contains(decision.CompetingInterpretations,
            x => x.Steps[0].Candidate.CanonicalName == "SupplierCertification.ValidTo >= today");

        // This is the failure mode the old top-1-wins Resolve() could not
        // distinguish from routing noise: it must also report Ambiguous here,
        // not quietly return whichever candidate scored a fraction higher.
        var resolved = new SemanticLexicalResolver(contract, source).Resolve("active");
        Assert.Equal(SemanticLexicalResolutionOutcome.Ambiguous, resolved.Outcome);
    }

    [Fact]
    public void Duplicate_evidence_for_the_same_supplier_relationship_does_not_trigger_a_false_ambiguity()
    {
        // Two retrieval strategies (say, a fuzzy match and a vector match)
        // both proposed the same "purchaseOrders" relationship for "supplied",
        // just with slightly different relevance scores. That is duplicate
        // evidence for one meaning, not two competing meanings.
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var purchaseOrders = SupplyChainSemanticModel.Relationship("Supplier", "purchaseOrders");

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "supplied", SemanticLexicalCandidateKind.Relationship, "purchaseOrders", .93,
                RelationshipId: purchaseOrders,
                SourceEntityId: SupplyChainSemanticModel.Supplier,
                TargetEntityId: SupplyChainSemanticModel.PurchaseOrder),
            new SemanticLexicalCandidate(
                "supplied", SemanticLexicalCandidateKind.Relationship, "purchaseOrders", .91,
                RelationshipId: purchaseOrders,
                SourceEntityId: SupplyChainSemanticModel.Supplier,
                TargetEntityId: SupplyChainSemanticModel.PurchaseOrder));

        var decision = new SemanticLexicalResolver(contract, source).Ground("supplied");

        Assert.Equal(GroundingOutcome.Committed, decision.Outcome);
        Assert.False(decision.HadCompetingMeanings);
        Assert.NotNull(decision.Committed);
        Assert.Equal("purchaseOrders", decision.Committed!.Steps[0].Candidate.CanonicalName);
        // The stronger of the two duplicate signals is the one that is kept.
        Assert.Equal(.93, decision.Committed.Steps[0].Candidate.Score);
    }

    private sealed class FakeLexicalSource(params SemanticLexicalCandidate[] candidates)
        : ISemanticLexicalCandidateSource
    {
        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request) =>
            candidates
                .Where(x => string.Equals(x.Token, request.Token, StringComparison.OrdinalIgnoreCase))
                .Where(x => request.EffectiveKinds.Contains(x.Kind))
                .OrderByDescending(x => x.Score)
                .ToArray();
    }
}