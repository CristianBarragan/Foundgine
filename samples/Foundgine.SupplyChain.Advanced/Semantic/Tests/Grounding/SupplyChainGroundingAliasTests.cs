using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Grounding;

/// <summary>
/// Case study for the README / walkthrough headline example — "show me overdue
/// purchase orders from our top supplier in Texas" — run against the real
/// generated Supply Chain semantic contract, the same contract
/// <c>find_top_supplier_overdue_orders</c> resolves against in
/// <c>Semantic/Api/Mcp/Program.cs</c>.
///
/// Unlike <see cref="SupplyChainGroundingAmbiguityTests"/> and
/// <see cref="SupplyChainGroundingUnresolvedTests"/>, which hand-write fake
/// candidates, this suite proves the alias declarations actually live in
/// <c>Semantic/Domain/Domain.cs</c>:
///   - <c>[FoundgineEntity("Supplier", ...)] [FoundgineAlias("Vendor")]
///     [FoundgineAlias("Seller")]</c>
///   - <c>[FoundgineEntity("PurchaseOrder", ...)] [FoundgineAlias(["PO", "POs"])]
///     [FoundgineAlias(["Buy", "Buys"])]</c>
/// and that they survive AOT metadata generation into
/// <see cref="SemanticLexiconProjection"/> output, and from there into
/// <see cref="SemanticLexicalResolver.Ground(string)"/> committing "seller"/"buys" to
/// the exact same interpretation as "supplier"/"PurchaseOrder" — a paraphrase
/// like "show me overdue buys from our top seller in Texas" is not a
/// coincidentally-similar second meaning, it is the declared alias path to
/// the one meaning "purchase orders"/"supplier" already commit to.
/// </summary>
public sealed class SupplyChainGroundingAliasTests
{
    [Fact]
    public void Domain_alias_attributes_are_projected_into_the_real_generated_lexicon()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var lexicon = SemanticLexiconProjection.Build(contract);

        var supplierEntry = Assert.Single(lexicon, x =>
            x.Kind == SemanticLexicalCandidateKind.Entity && x.CanonicalName == "Supplier");
        Assert.Contains("Vendor", supplierEntry.EffectiveAliases);
        Assert.Contains("Seller", supplierEntry.EffectiveAliases);

        var purchaseOrderEntry = Assert.Single(lexicon, x =>
            x.Kind == SemanticLexicalCandidateKind.Entity && x.CanonicalName == "PurchaseOrder");
        Assert.Contains("PO", purchaseOrderEntry.EffectiveAliases);
        Assert.Contains("Buys", purchaseOrderEntry.EffectiveAliases);
    }

    [Fact]
    public void Retrieval_representations_for_an_entity_are_collapsed_before_resolution()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var resolver = new SemanticLexicalResolver(
            contract, new AliasAwareLexicalSource(SemanticLexiconProjection.Build(contract)));

        // The projection intentionally contains both an Entity and a Node
        // document for each entity. They are retrieval representations of the
        // same semantic identity and must not become competing interpretations.
        var sellerCandidates = resolver.GetCandidates("seller")["seller"];
        var buysCandidates = resolver.GetCandidates("buys")["buys"];

        Assert.Single(sellerCandidates);
        Assert.Equal(SemanticLexicalCandidateKind.Entity, sellerCandidates[0].Kind);
        Assert.Equal(SupplyChainSemanticModel.Supplier, sellerCandidates[0].EntityId);

        Assert.Single(buysCandidates);
        Assert.Equal(SemanticLexicalCandidateKind.Entity, buysCandidates[0].Kind);
        Assert.Equal(SupplyChainSemanticModel.PurchaseOrder, buysCandidates[0].EntityId);
    }

    [Fact]
    public void Exact_canonical_entity_names_win_over_same_named_relationship_roots()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var resolver = new SemanticLexicalResolver(
            contract, new AliasAwareLexicalSource(SemanticLexiconProjection.Build(contract)));

        var supplier = resolver.Ground("Supplier");
        var purchaseOrder = resolver.Ground("PurchaseOrder");

        Assert.Equal(GroundingOutcome.Committed, supplier.Outcome);
        Assert.Equal(SupplyChainSemanticModel.Supplier, supplier.Committed!.RootEntity);
        Assert.Equal("Supplier", supplier.Committed.Steps.Single().Candidate.CanonicalName);

        Assert.Equal(GroundingOutcome.Committed, purchaseOrder.Outcome);
        Assert.Equal(SupplyChainSemanticModel.PurchaseOrder, purchaseOrder.Committed!.RootEntity);
        Assert.Equal("PurchaseOrder", purchaseOrder.Committed.Steps.Single().Candidate.CanonicalName);
    }

    [Fact]
    public void Seller_grounds_to_the_same_interpretation_as_supplier()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var resolver = new SemanticLexicalResolver(
            contract, new AliasAwareLexicalSource(SemanticLexiconProjection.Build(contract)));

        var canonical = resolver.Ground("Supplier");
        var alias = resolver.Ground("seller");

        Assert.Equal(GroundingOutcome.Committed, canonical.Outcome);
        Assert.Equal(GroundingOutcome.Committed, alias.Outcome);
        Assert.Equal(SupplyChainSemanticModel.Supplier, canonical.Committed!.RootEntity);
        Assert.Equal(SupplyChainSemanticModel.Supplier, alias.Committed!.RootEntity);
        Assert.Equal(canonical.Committed.Signature, alias.Committed.Signature);
    }

    [Fact]
    public void Readme_paraphrase_grounds_the_same_semantic_identities_as_the_canonical_request()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var resolver = new SemanticLexicalResolver(
            contract, new AliasAwareLexicalSource(SemanticLexiconProjection.Build(contract)));

        // README canonical request:
        // "show me overdue purchase orders from our top supplier in Texas"
        // README paraphrase:
        // "show me the overdue buys from our top seller in Texas"
        //
        // Grounding owns the vocabulary-to-meaning step. The other words are
        // handled by the operation graph / retrieval stages, so this test
        // deliberately proves the two content-bearing aliases against their
        // canonical semantic identities.
        var canonicalPurchaseOrder = resolver.Ground("purchase order");
        var aliasPurchaseOrder = resolver.Ground("buys");
        var canonicalSupplier = resolver.Ground("supplier");
        var aliasSupplier = resolver.Ground("seller");

        Assert.Equal(GroundingOutcome.Committed, canonicalPurchaseOrder.Outcome);
        Assert.Equal(GroundingOutcome.Committed, aliasPurchaseOrder.Outcome);
        Assert.Equal(GroundingOutcome.Committed, canonicalSupplier.Outcome);
        Assert.Equal(GroundingOutcome.Committed, aliasSupplier.Outcome);

        Assert.Equal(SupplyChainSemanticModel.PurchaseOrder, canonicalPurchaseOrder.Committed!.RootEntity);
        Assert.Equal(SupplyChainSemanticModel.PurchaseOrder, aliasPurchaseOrder.Committed!.RootEntity);
        Assert.Equal(canonicalPurchaseOrder.Committed.Signature, aliasPurchaseOrder.Committed.Signature);

        Assert.Equal(SupplyChainSemanticModel.Supplier, canonicalSupplier.Committed!.RootEntity);
        Assert.Equal(SupplyChainSemanticModel.Supplier, aliasSupplier.Committed!.RootEntity);
        Assert.Equal(canonicalSupplier.Committed.Signature, aliasSupplier.Committed.Signature);
    }

    [Fact]
    public void Buys_grounds_to_the_same_interpretation_as_purchase_order()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var resolver = new SemanticLexicalResolver(
            contract, new AliasAwareLexicalSource(SemanticLexiconProjection.Build(contract)));

        var canonical = resolver.Ground("PurchaseOrder");
        var alias = resolver.Ground("buys");

        Assert.Equal(GroundingOutcome.Committed, canonical.Outcome);
        Assert.Equal(GroundingOutcome.Committed, alias.Outcome);
        Assert.Equal(SupplyChainSemanticModel.PurchaseOrder, canonical.Committed!.RootEntity);
        Assert.Equal(SupplyChainSemanticModel.PurchaseOrder, alias.Committed!.RootEntity);
        Assert.Equal(canonical.Committed.Signature, alias.Committed.Signature);
    }

    /// <summary>
    /// Stand-in retrieval provider that matches a token against either an
    /// entry's canonical name or any of its declared aliases — the same
    /// lookup an Elasticsearch/pgvector index built from
    /// <see cref="SemanticLexiconProjection"/> output performs. Not a set of
    /// hand-picked fake candidates: it reads only what
    /// <see cref="SemanticLexiconProjection.Build"/> actually derived from
    /// the real contract, so "seller"/"buys" only resolve here because
    /// Domain.cs declared them.
    /// </summary>
    private sealed class AliasAwareLexicalSource(IReadOnlyList<SemanticLexiconEntry> lexicon)
        : ISemanticLexicalCandidateSource
    {
        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request) =>
            lexicon
                .Where(entry => request.EffectiveKinds.Contains(entry.Kind))
                .Where(entry =>
                    string.Equals(entry.CanonicalName, request.Token, StringComparison.OrdinalIgnoreCase) ||
                    entry.EffectiveAliases.Any(a =>
                        string.Equals(a, request.Token, StringComparison.OrdinalIgnoreCase)))
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