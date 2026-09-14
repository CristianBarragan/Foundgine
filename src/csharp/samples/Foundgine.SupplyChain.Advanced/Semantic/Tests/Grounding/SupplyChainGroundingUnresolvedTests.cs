using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Grounding;

/// <summary>
/// Case study for the two "fails closed on missing vocabulary/capability"
/// adversarial examples from docs/LEXICAL-GROUNDING.md ("customers with big
/// accounts" and "... last summer"), run against the real generated Supply
/// Chain semantic contract rather than the toy model used in the core
/// library's <c>SemanticLexicalResolverTests</c>.
///
/// Both examples in the doc are refusals for a different reason than the
/// tied-confidence case in <see cref="SupplyChainGroundingAmbiguityTests"/>:
/// here there is no candidate for the token at all, so the resolver has
/// nothing to be ambiguous about. It must report
/// <see cref="GroundingOutcome.Unresolved"/> and name the exact token that
/// had no candidate, rather than silently dropping that part of the
/// expression and executing the rest.
/// </summary>
public sealed class SupplyChainGroundingUnresolvedTests
{
    [Fact]
    public void Unresolved_when_a_business_threshold_term_has_no_declared_vocabulary()
    {
        // Mirrors "customers with big accounts": Supplier.RiskScore is a raw
        // decimal field, not a named tier or flag (there is no
        // Supplier.IsHighRisk, no "RiskTier" value vocabulary), so a
        // business-threshold word like "risky" is not a field, value,
        // relationship, or entity anywhere in the contract. Grounding must
        // not invent a threshold (RiskScore > what?) to make the query
        // runnable — it must refuse and name the token.
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "suppliers", SemanticLexicalCandidateKind.Entity, "Supplier", .95,
                EntityId: SupplyChainSemanticModel.Supplier));

        var decision = new SemanticLexicalResolver(contract, source).Ground("risky suppliers");

        Assert.Equal(GroundingOutcome.Unresolved, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
        Assert.Contains("risky", decision.Reason);

        // Resolve() must surface the same refusal, not silently fall back to
        // "suppliers" alone and drop the threshold the caller asked for.
        var resolved = new SemanticLexicalResolver(contract, source).Resolve("risky suppliers");
        Assert.Equal(SemanticLexicalResolutionOutcome.Unresolved, resolved.Outcome);
    }

    [Fact]
    public void Unresolved_when_no_temporal_candidate_kind_exists_for_a_relative_date_range()
    {
        // Mirrors "Nike customers who bought running shoes last summer":
        // SemanticLexicalCandidateKind has no temporal kind at all (Entity,
        // Node, Relationship, Traversal, Field, Value, Operation — none of
        // those represents a relative date range), so "last month" cannot
        // resolve to anything even though "shipments" resolves fine on its
        // own. The whole expression must fail closed rather than silently
        // resolving the entity portion and dropping the time constraint.
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "shipments", SemanticLexicalCandidateKind.Entity, "Shipment", .95,
                EntityId: SupplyChainSemanticModel.Shipment),
            new SemanticLexicalCandidate(
                "delayed", SemanticLexicalCandidateKind.Value, "Shipment.Status = Delayed", .93,
                EntityId: SupplyChainSemanticModel.Shipment,
                FieldId: SupplyChainSemanticModel.Field("Shipment", "Status"),
                Value: "Delayed"));

        var decision = new SemanticLexicalResolver(contract, source).Ground("delayed shipments last month");

        Assert.Equal(GroundingOutcome.Unresolved, decision.Outcome);
        Assert.Null(decision.Committed);
        // "delayed" and "shipments" both had candidates; "last" is the first
        // token with none, so it is the one named in the refusal reason.
        Assert.Contains("last", decision.Reason);
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
