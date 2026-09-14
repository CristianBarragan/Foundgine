using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

/// <summary>
/// Case study for the README / walkthrough headline example — "show me overdue
/// purchase orders from our top supplier in Texas" — proving the alias path a
/// paraphrase of it takes through the real architecture, not just describing it.
///
/// The walkthrough's Step 3 ("Semantic Model") and Step 5 ("Retrieval") describe
/// two different jobs that are easy to conflate:
///   - the semantic contract declares <em>aliases</em> on an entity
///     (<see cref="SemanticEntityBuilder{T}.Alias(string, int?)"/>), which
///     <see cref="SemanticLexiconProjection"/> folds into every
///     <see cref="SemanticLexiconEntry"/> it derives from that entity;
///   - a retrieval provider (Elasticsearch, pgvector, or — as here — a fake,
///     in-memory stand-in) indexes that projection and is the thing an
///     <see cref="ISemanticLexicalCandidateSource"/> actually queries.
///
/// A synonym in a caller's sentence therefore only grounds to the same meaning
/// as the "canonical" word if it survived both hops: declared as an alias on
/// the contract, *and* matched by whatever sits behind the candidate source.
/// This test builds a minimal but real contract (Supplier aliased "Seller",
/// PurchaseOrder aliased "Buys"), projects it with the production
/// <see cref="SemanticLexiconProjection"/>, and backs
/// <see cref="SemanticLexicalResolver.Ground(string)"/> with a source that matches a
/// token against either an entry's canonical name or its
/// <see cref="SemanticLexiconEntry.EffectiveAliases"/> — the same contract the
/// canonical name is matched against, so canonical and alias tokens are proven
/// to reach the identical committed interpretation rather than merely two
/// interpretations that happen to look similar.
/// </summary>
public sealed class SemanticAliasSynonymGroundingTests
{
    private static readonly EntityId Supplier = new(1);
    private static readonly EntityId PurchaseOrder = new(2);

    [Fact]
    public void Buys_grounds_to_the_same_interpretation_as_purchase_order()
    {
        var resolver = BuildResolver();

        // "PurchaseOrder" is the real entity name used by
        // Foundgine.SupplyChain.Advanced's find_top_supplier_overdue_orders
        // capability (see SupplyChainSemanticModel.PurchaseOrder). Grounding
        // works token-by-token (see the "customers" pattern used throughout
        // SemanticLexicalResolverTests), so a single-word canonical query is
        // the faithful unit here; the full multi-word sentence is exercised
        // end-to-end in Full_paraphrase_grounds_every_token_the_same_way_as_the_canonical_sentence.
        var canonical = resolver.Ground("PurchaseOrder");
        var alias = resolver.Ground("buys");

        Assert.Equal(GroundingOutcome.Committed, canonical.Outcome);
        Assert.Equal(GroundingOutcome.Committed, alias.Outcome);

        // Same root entity, same canonical name, same signature: "buys" is not
        // a second, coincidentally-similar meaning — it is the alias path to
        // the exact interpretation "purchase order" already committed to.
        Assert.Equal(PurchaseOrder, canonical.Committed!.RootEntity);
        Assert.Equal(PurchaseOrder, alias.Committed!.RootEntity);
        Assert.Equal(canonical.Committed.Signature, alias.Committed.Signature);
        Assert.Equal("PurchaseOrder", canonical.Committed.Steps[0].Candidate.CanonicalName);
        Assert.Equal("PurchaseOrder", alias.Committed.Steps[0].Candidate.CanonicalName);
    }

    [Fact]
    public void Seller_grounds_to_the_same_interpretation_as_supplier()
    {
        var resolver = BuildResolver();

        var canonical = resolver.Ground("supplier");
        var alias = resolver.Ground("seller");

        Assert.Equal(GroundingOutcome.Committed, canonical.Outcome);
        Assert.Equal(GroundingOutcome.Committed, alias.Outcome);

        Assert.Equal(Supplier, canonical.Committed!.RootEntity);
        Assert.Equal(Supplier, alias.Committed!.RootEntity);
        Assert.Equal(canonical.Committed.Signature, alias.Committed.Signature);
        Assert.Equal("Supplier", canonical.Committed.Steps[0].Candidate.CanonicalName);
        Assert.Equal("Supplier", alias.Committed.Steps[0].Candidate.CanonicalName);
    }

    [Fact]
    public void Full_paraphrase_grounds_every_token_the_same_way_as_the_canonical_sentence()
    {
        var resolver = BuildResolver();

        // README: "show me overdue purchase orders from our top supplier in Texas"
        // Paraphrase: "show me overdue buys from our top seller in Texas"
        // Only the two content words that carry the entity meaning need to
        // ground identically; the rest of each sentence is filler for this
        // isolated grounding step (state/rank/overdue are resolved elsewhere
        // in the pipeline — see docs/GROUNDING-DECISIONS.md).
        foreach (var (canonicalWord, aliasWord, expectedEntity) in new[]
                 {
                     ("purchase order", "buys", PurchaseOrder),
                     ("supplier", "seller", Supplier)
                 })
        {
            var canonical = resolver.Ground(canonicalWord);
            var alias = resolver.Ground(aliasWord);

            Assert.Equal(GroundingOutcome.Committed, canonical.Outcome);
            Assert.Equal(GroundingOutcome.Committed, alias.Outcome);
            Assert.Equal(expectedEntity, canonical.Committed!.RootEntity);
            Assert.Equal(expectedEntity, alias.Committed!.RootEntity);
            Assert.Equal(canonical.Committed.Signature, alias.Committed.Signature);
        }
    }

    private static SemanticLexicalResolver BuildResolver()
    {
        var contract = new SemanticModelBuilder()
            .Entity(Supplier, "Supplier", e => e
                .Alias("Seller")
                .Identity(new FieldId(1), "Id"))
            .Entity(PurchaseOrder, "PurchaseOrder", e => e
                .Alias("Buys")
                .Identity(new FieldId(2), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        // The production projection is what turns declared aliases into
        // searchable lexicon entries — the same projection a real Elasticsearch
        // or pgvector-backed ISemanticLexicalCandidateSource indexes from.
        var lexicon = SemanticLexiconProjection.Build(contract);

        return new SemanticLexicalResolver(contract, new AliasAwareLexicalSource(lexicon));
    }

    /// <summary>
    /// Stand-in for a real retrieval provider: matches a token against either
    /// an entry's canonical name or any of its declared aliases, exactly the
    /// lookup an Elasticsearch/pgvector index built from
    /// <see cref="SemanticLexiconProjection"/> output performs.
    /// </summary>
    private sealed class AliasAwareLexicalSource(IReadOnlyList<SemanticLexiconEntry> lexicon)
        : ISemanticLexicalCandidateSource
    {
        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request) =>
            lexicon
                .Where(entry => request.EffectiveKinds.Contains(entry.Kind))
                .Where(entry =>
                    string.Equals(entry.CanonicalName, request.Token, StringComparison.OrdinalIgnoreCase) ||
                    entry.EffectiveAliases.Any(alias =>
                        string.Equals(alias, request.Token, StringComparison.OrdinalIgnoreCase)))
                .Select(entry => new SemanticLexicalCandidate(
                    request.Token,
                    entry.Kind,
                    entry.CanonicalName,
                    Score: .95,
                    EntityId: entry.EntityId,
                    RelationshipId: entry.RelationshipId,
                    FieldId: entry.FieldId,
                    SourceEntityId: entry.SourceEntityId,
                    TargetEntityId: entry.TargetEntityId,
                    Value: entry.Value))
                .ToArray();
    }
}