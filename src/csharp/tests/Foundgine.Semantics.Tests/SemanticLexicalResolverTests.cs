using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using System.Threading;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticLexicalResolverTests
{
    [Fact]
    public void Resolver_generates_all_kinds_and_uses_highest_root_before_graph_constrained_walk()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var line = new EntityId(3);
        var product = new EntityId(4);
        var category = new EntityId(5);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Entity(order, "SalesOrder", e => e.Identity(new FieldId(201), "Id"))
            .Entity(line, "SalesOrderLine", e => e.Identity(new FieldId(301), "Id"))
            .Entity(product, "CatalogProduct", e => e
                .Identity(new FieldId(401), "Id")
                .Field(new FieldId(402), "Name", typeof(string)))
            .Entity(category, "Category", e => e
                .Identity(new FieldId(501), "Id")
                .Field(new FieldId(502), "Name", typeof(string)))
            .Relationship<Dummy, Dummy>(customer, new RelationshipId(1), "Orders", x => x.Id, order, x => x.Id,
                RelationshipCardinality.Many)
            .Relationship<Dummy, Dummy>(order, new RelationshipId(2), "Lines", x => x.Id, line, x => x.Id,
                RelationshipCardinality.Many)
            .Relationship<Dummy, Dummy>(line, new RelationshipId(3), "Product", x => x.Id, product, x => x.Id,
                RelationshipCardinality.One)
            .Relationship<Dummy, Dummy>(product, new RelationshipId(4), "Category", x => x.Id, category, x => x.Id,
                RelationshipCardinality.One)
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("bought", SemanticLexicalCandidateKind.Relationship, "Orders", .98,
                RelationshipId: new RelationshipId(1), SourceEntityId: customer, TargetEntityId: order),
            new SemanticLexicalCandidate("bought", SemanticLexicalCandidateKind.Operation, "Buy", .995),
            new SemanticLexicalCandidate("nike", SemanticLexicalCandidateKind.Value, "Nike", .99,
                EntityId: product, FieldId: new FieldId(402), Value: "Nike"),
            new SemanticLexicalCandidate("shoes", SemanticLexicalCandidateKind.Value, "Shoes", .97,
                EntityId: category, FieldId: new FieldId(502), Value: "Shoes"));

        var result = new SemanticLexicalResolver(model, source).Resolve("bought nike shoes");

        Assert.Equal(SemanticLexicalResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal(customer, result.RootEntity);
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("Orders", result.Steps[0].Candidate.CanonicalName);
        Assert.Equal("Nike", result.Steps[1].Candidate.CanonicalName);
        Assert.Equal("Shoes", result.Steps[2].Candidate.CanonicalName);
        Assert.Equal(2, result.Steps[1].BridgingPath.Count);
        Assert.Single(result.Steps[2].BridgingPath);
        Assert.Contains(source.Requests,
            x => x.Token == "bought" &&
                 x.EffectiveKinds.Count == Enum.GetValues<SemanticLexicalCandidateKind>().Length);
    }

    [Fact]
    public void Resolver_backtracks_when_highest_lexical_root_cannot_form_a_complete_path()
    {
        var customer = new EntityId(1);
        var product = new EntityId(2);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Entity(product, "Product", e => e.Identity(new FieldId(201), "Id"))
            .Relationship<Dummy, Dummy>(customer, new RelationshipId(1), "Orders", x => x.Id, product, x => x.Id,
                RelationshipCardinality.Many)
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("acquired", SemanticLexicalCandidateKind.Relationship, "Wrong", .99,
                RelationshipId: new RelationshipId(99), SourceEntityId: new EntityId(99), TargetEntityId: product),
            new SemanticLexicalCandidate("acquired", SemanticLexicalCandidateKind.Relationship, "Orders", .85,
                RelationshipId: new RelationshipId(1), SourceEntityId: customer, TargetEntityId: product),
            new SemanticLexicalCandidate("shoes", SemanticLexicalCandidateKind.Value, "Shoes", .95,
                EntityId: product, FieldId: new FieldId(201), Value: "Shoes"));

        var result = new SemanticLexicalResolver(model, source).Resolve("acquired shoes");

        Assert.Equal(SemanticLexicalResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal("Orders", result.Steps[0].Candidate.CanonicalName);
        Assert.Equal(customer, result.RootEntity);
    }

    [Fact]
    public void Ground_requires_clarification_when_a_token_maps_to_two_different_fields_with_tied_confidence()
    {
        // "Show me active customers" — "active" is structurally valid against
        // either field. That is a competing *meaning*, not a routing artifact,
        // so Foundgine must not silently pick one.
        var customer = new EntityId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Field(new FieldId(601), "AccountEnabled", typeof(bool))
                .Field(new FieldId(602), "HasRecentOrder", typeof(bool)))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "AccountEnabled", .90,
                EntityId: customer, FieldId: new FieldId(601), Value: "true"),
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "HasRecentOrder", .89,
                EntityId: customer, FieldId: new FieldId(602), Value: "true"));

        var decision = new SemanticLexicalResolver(model, source).Ground("active");

        Assert.Equal(GroundingOutcome.RequiresClarification, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.True(decision.HadCompetingMeanings);
        Assert.Equal(2, decision.CompetingInterpretations.Count);
        Assert.Contains(decision.CompetingInterpretations, x => x.Steps[0].Candidate.CanonicalName == "AccountEnabled");
        Assert.Contains(decision.CompetingInterpretations, x => x.Steps[0].Candidate.CanonicalName == "HasRecentOrder");

        // Resolve() must reflect the same uncertainty, not quietly pick the winner.
        var resolved = new SemanticLexicalResolver(model, source).Resolve("active");
        Assert.Equal(SemanticLexicalResolutionOutcome.Ambiguous, resolved.Outcome);
    }

    [Fact]
    public void Ground_does_not_report_ambiguity_when_tied_paths_are_the_same_meaning_via_different_evidence()
    {
        // Two retrieval sources (say, BM25 and a vector index) both proposed the
        // exact same relationship with slightly different scores. That is
        // duplicate evidence for one meaning, not two competing meanings, and
        // must not force a clarification the user has no reason to answer.
        var customer = new EntityId(1);
        var product = new EntityId(2);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Entity(product, "Product", e => e.Identity(new FieldId(201), "Id"))
            .Relationship<Dummy, Dummy>(customer, new RelationshipId(1), "Orders", x => x.Id, product, x => x.Id,
                RelationshipCardinality.Many)
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("acquired", SemanticLexicalCandidateKind.Relationship, "Orders", .91,
                RelationshipId: new RelationshipId(1), SourceEntityId: customer, TargetEntityId: product),
            new SemanticLexicalCandidate("acquired", SemanticLexicalCandidateKind.Relationship, "Orders", .90,
                RelationshipId: new RelationshipId(1), SourceEntityId: customer, TargetEntityId: product));

        var decision = new SemanticLexicalResolver(model, source).Ground("acquired");

        Assert.Equal(GroundingOutcome.Committed, decision.Outcome);
        Assert.NotNull(decision.Committed);
        Assert.False(decision.HadCompetingMeanings);
        Assert.Equal("Orders", decision.Committed!.Steps[0].Candidate.CanonicalName);
        // The higher-scoring duplicate should be the one retained.
        Assert.Equal(.91, decision.Committed.Steps[0].Candidate.Score);
    }

    [Fact]
    public void Ground_reports_unresolved_when_a_token_has_no_candidates()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("customers", SemanticLexicalCandidateKind.Entity, "Customer", .95,
                EntityId: customer));

        var decision = new SemanticLexicalResolver(model, source).Ground("customers zzznotaword");

        Assert.Equal(GroundingOutcome.Unresolved, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
        Assert.Contains("zzznotaword", decision.Reason);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_expression_exceeds_max_tokens()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("customers", SemanticLexicalCandidateKind.Entity, "Customer", .95,
                EntityId: customer));

        // maxTokens: 3 — the four-token expression must be refused before any
        // retrieval or search runs, not silently truncated or best-effort resolved.
        var resolver = new SemanticLexicalResolver(model, source, maxTokens: 3);
        var decision = resolver.Ground("show me all customers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.MaxTokens, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
        Assert.Empty(source.Requests); // no retrieval happened — refused up front
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_search_exceeds_max_paths_explored()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Field(new FieldId(601), "Status", typeof(string)))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("show", SemanticLexicalCandidateKind.Entity, "Customer", .90,
                EntityId: customer),
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "Status", .90,
                EntityId: customer, FieldId: new FieldId(601), Value: "active"));

        // maxPathsExplored: 1 — the search must stop and fail closed rather
        // than continue past the configured work ceiling.
        var resolver = new SemanticLexicalResolver(model, source, maxPathsExplored: 1);
        var decision = resolver.Ground("show active");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.MaxPathsExplored, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_on_timeout()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("customers", SemanticLexicalCandidateKind.Entity, "Customer", .95,
                EntityId: customer));

        // A one-tick timeout is effectively already elapsed by the time the
        // search's first budget check runs, so this is deterministic rather
        // than a flaky wall-clock race.
        var resolver = new SemanticLexicalResolver(model, source, timeout: TimeSpan.FromTicks(1));
        var decision = resolver.Ground("customers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.Timeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_cancellation_is_requested()
    {
        var customer = new EntityId(1);
        var product = new EntityId(2);
        var category = new EntityId(3);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Entity(product, "CatalogProduct", e => e
                .Identity(new FieldId(401), "Id")
                .Field(new FieldId(402), "Name", typeof(string)))
            .Entity(category, "Category", e => e
                .Identity(new FieldId(501), "Id")
                .Field(new FieldId(502), "Name", typeof(string)))
            .Relationship<Dummy, Dummy>(customer, new RelationshipId(1), "Orders", x => x.Id, product, x => x.Id,
                RelationshipCardinality.Many)
            .Relationship<Dummy, Dummy>(product, new RelationshipId(2), "Category", x => x.Id, category, x => x.Id,
                RelationshipCardinality.One)
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("bought", SemanticLexicalCandidateKind.Relationship, "Orders", .98,
                RelationshipId: new RelationshipId(1), SourceEntityId: customer, TargetEntityId: product),
            new SemanticLexicalCandidate("nike", SemanticLexicalCandidateKind.Value, "Nike", .99,
                EntityId: product, FieldId: new FieldId(402), Value: "Nike"),
            new SemanticLexicalCandidate("shoes", SemanticLexicalCandidateKind.Value, "Shoes", .97,
                EntityId: category, FieldId: new FieldId(502), Value: "Shoes"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var resolver = new SemanticLexicalResolver(model, source);
        var decision = resolver.Ground("bought nike shoes", cts.Token);

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.Cancelled, decision.BudgetLimit);
        Assert.Null(decision.Committed);

        // Resolve() must surface the same fail-closed signal, not "resolved
        // to whatever was found before cancellation."
        var resolved = new SemanticLexicalResolver(model, source).Resolve("bought nike shoes", cts.Token);
        Assert.Equal(SemanticLexicalResolutionOutcome.BudgetExceeded, resolved.Outcome);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_retrieval_exceeds_retrieval_timeout()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        // A candidate source that simulates a slow/hung provider (network
        // partition, slow index). The source is intentionally slower than the
        // deadline; the resolver must fail closed even when the provider ignores
        // cancellation and returns only after the deadline has elapsed.
        var source = new SlowLexicalSource(TimeSpan.FromMilliseconds(20));
        var resolver = new SemanticLexicalResolver(model, source, retrievalTimeout: TimeSpan.FromTicks(1));

        var decision = resolver.Ground("customers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
    }

    [Fact]
    public void Ground_detects_retrieval_timeout_after_a_provider_returns()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        // This source deliberately ignores cancellation and blocks longer than
        // the configured retrieval deadline. The resolver must check the shared
        // deadline after Retrieve returns rather than silently accepting the result.
        var source = new SlowLexicalSource(TimeSpan.FromMilliseconds(20));
        var resolver = new SemanticLexicalResolver(model, source, retrievalTimeout: TimeSpan.FromMilliseconds(5));

        var decision = resolver.Ground("customers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
    }

    [Fact]
    public void Ground_uses_one_retrieval_deadline_across_compact_token_fallback()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(101), "Id"))
            .Build()
            .Freeze()
            .CreateSnapshot();

        // The invariant under test is that the compact-token fallback shares
        // the original retrieval deadline; it must not receive a fresh timeout.
        // Keep the initial token lookup well inside the overall budget, then
        // make the compact fallback exceed only the *remaining* budget.
        // A fresh timeout for the fallback would incorrectly allow it to
        // complete; the shared deadline must reject it.
        //
        // The margins here are deliberately wide (well beyond Windows' ~15ms
        // default thread-timer resolution, which can silently round a
        // Thread.Sleep(1) up to that much) so the assertions don't flake
        // depending on OS scheduling: the first lookup sleeps for 0ms (no
        // rounding risk at all), and the fallback's 500ms is far past any
        // plausible scheduler jitter on top of the 200ms budget.
        var source = new SequencedSlowEmptyLexicalSource(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500));
        var resolver = new SemanticLexicalResolver(model, source, retrievalTimeout: TimeSpan.FromMilliseconds(200));

        var decision = resolver.Ground("customer profile");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Equal(2, source.Requests.Count);
        Assert.Contains(source.Requests[0].Token, new[] { "customer", "profile" });
        Assert.Equal("customerprofile", source.Requests[1].Token);
    }

    [Fact]
    public void Ground_exposes_partial_interpretations_on_budget_exceeded_but_never_commits_them()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Field(new FieldId(601), "Status", typeof(string)))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("show", SemanticLexicalCandidateKind.Entity, "Customer", .90,
                EntityId: customer),
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "Status", .90,
                EntityId: customer, FieldId: new FieldId(601), Value: "active"));

        // Generous enough to let at least one full path complete before the
        // budget trips on the next unit of work, so PartialInterpretationsAtCutoff
        // is exercised rather than always empty.
        var resolver = new SemanticLexicalResolver(model, source, maxPathsExplored: 3);
        var decision = resolver.Ground("show active");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
        // Diagnostic-only: whatever was found is inspectable, but never
        // treated as authorizable — Committed stays null above regardless
        // of how many partial interpretations were captured.
        Assert.NotNull(decision.EffectivePartialInterpretationsAtCutoff);
    }

    [Fact]
    public void Ground_refuses_commit_when_unique_interpretation_has_insufficient_alias_weight()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Alias("Buyer", 40))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("Buyer", SemanticLexicalCandidateKind.Entity, "Customer", .99,
                EntityId: customer));

        var decision = new SemanticLexicalResolver(model, source, minimumAliasWeight: 80).Ground("Buyer");

        Assert.Equal(GroundingOutcome.RequiresClarification, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.False(decision.HadCompetingMeanings);
        Assert.Contains("minimum weight", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(decision.AliasEvidence);
        Assert.Equal(AliasEvidenceStatus.Insufficient, decision.AliasEvidence!.Status);
        Assert.Equal(40, decision.AliasEvidence.EntityWeights[customer]);
    }

    [Fact]
    public void Ground_commits_unique_interpretation_when_alias_weight_meets_threshold_even_if_retrieval_score_differs()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Alias("Buyer", 90))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("Buyer", SemanticLexicalCandidateKind.Entity, "Customer", .60,
                EntityId: customer));

        var decision = new SemanticLexicalResolver(model, source, minimumAliasWeight: 80).Ground("Buyer");

        Assert.Equal(GroundingOutcome.Committed, decision.Outcome);
        Assert.NotNull(decision.Committed);
        Assert.Equal(90, decision.Committed!.EffectiveAliasEvidence.EntityWeights[customer]);
        Assert.Equal(AliasEvidenceStatus.Sufficient, decision.Committed.EffectiveAliasEvidence.Status);
    }

    [Fact]
    public void Alias_weight_never_changes_which_interpretation_wins_only_whether_the_winner_may_commit()
    {
        // Alias weights are a post-ranking commitment gate, not a retrieval
        // ranking signal. The same retrieval candidates are therefore evaluated
        // against two otherwise equivalent contracts: in the first contract
        // the retrieval winner has weak alias evidence and cannot commit; in the
        // second it has strong alias evidence and can commit. The lower-scoring
        // interpretation never gets to win merely because its alias weight is
        // higher.
        var customer = new EntityId(1);
        var vendor = new EntityId(2);

        SemanticContractSnapshot BuildContract(int customerAliasWeight)
            => new SemanticModelBuilder()
                .Entity(customer, "Customer", e => e
                    .Identity(new FieldId(101), "Id")
                    .Alias("Party", customerAliasWeight))
                .Entity(vendor, "Vendor", e => e
                    .Identity(new FieldId(201), "Id"))
                .Build()
                .Freeze()
                .CreateSnapshot();

        var sourceCandidates = new[]
        {
            new SemanticLexicalCandidate("Party", SemanticLexicalCandidateKind.Entity, "Customer", .97,
                EntityId: customer),
            new SemanticLexicalCandidate("Party", SemanticLexicalCandidateKind.Entity, "Vendor", .40,
                EntityId: vendor)
        };

        var weakDecision = new SemanticLexicalResolver(
                BuildContract(customerAliasWeight: 20),
                new FakeLexicalSource(sourceCandidates),
                minimumAliasWeight: 50)
            .Ground("Party");

        // Retrieval score alone decided the winner: Customer, not Vendor.
        // The winner then fails the alias-weight commitment gate.
        Assert.Equal(GroundingOutcome.RequiresClarification, weakDecision.Outcome);
        Assert.False(weakDecision.HadCompetingMeanings);
        Assert.Null(weakDecision.Committed);
        Assert.NotNull(weakDecision.AliasEvidence);
        Assert.Equal(AliasEvidenceStatus.Insufficient, weakDecision.AliasEvidence!.Status);
        Assert.Equal(20, weakDecision.AliasEvidence.EntityWeights[customer]);
        Assert.DoesNotContain(vendor, weakDecision.AliasEvidence.EntityWeights.Keys);

        var strongDecision = new SemanticLexicalResolver(
                BuildContract(customerAliasWeight: 95),
                new FakeLexicalSource(sourceCandidates),
                minimumAliasWeight: 50)
            .Ground("Party");

        // Increasing the winner's alias weight changes only commitment. It
        // does not change which interpretation won retrieval ranking.
        Assert.Equal(GroundingOutcome.Committed, strongDecision.Outcome);
        Assert.NotNull(strongDecision.Committed);
        Assert.Equal(customer, strongDecision.Committed!.RootEntity);
        Assert.Equal(AliasEvidenceStatus.Sufficient, strongDecision.AliasEvidence!.Status);
        Assert.Equal(95, strongDecision.AliasEvidence.EntityWeights[customer]);
        Assert.DoesNotContain(vendor, strongDecision.AliasEvidence.EntityWeights.Keys);
    }

    [Fact]
    public void Ground_requires_clarification_when_a_close_candidate_is_cut_by_the_candidate_limit()
    {
        // Same shape as the classic "active customers" tied-confidence case,
        // except here the second field never survives to graph search at
        // all: candidateLimit trims it away at retrieval time. Foundgine
        // must not treat "graph search saw no competing meaning" as proof
        // that none existed — the cut candidate was never checked.
        var customer = new EntityId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Field(new FieldId(601), "AccountEnabled", typeof(bool))
                .Field(new FieldId(602), "HasRecentOrder", typeof(bool)))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "AccountEnabled", .90,
                EntityId: customer, FieldId: new FieldId(601), Value: "true"),
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "HasRecentOrder", .89,
                EntityId: customer, FieldId: new FieldId(602), Value: "true"));

        var decision = new SemanticLexicalResolver(model, source, candidateLimit: 1).Ground("active");

        Assert.Equal(GroundingOutcome.RequiresClarification, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.Single(decision.EffectiveTruncationRisks);
        Assert.Equal(2, source.Requests[0].Limit); // candidateLimit + 1 sentinel

        var risk = decision.EffectiveTruncationRisks[0];
        Assert.Equal("active", risk.Token);
        Assert.Equal(1, risk.RetainedCount);
        Assert.Equal(1, risk.TruncatedCount);
        Assert.Equal(.90, risk.LowestRetainedScore);
        Assert.Equal(.89, risk.HighestTruncatedScore);
        Assert.True(risk.WithinAmbiguityMargin(0.03));
    }

    [Fact]
    public void Ground_commits_when_a_truncated_candidate_is_well_outside_the_ambiguity_margin()
    {
        // Truncation alone is not the problem — it is unavoidable and
        // configured deliberately. Only a cut candidate close enough to have
        // plausibly been a competing meaning should block commitment.
        var customer = new EntityId(1);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(101), "Id")
                .Field(new FieldId(601), "AccountEnabled", typeof(bool))
                .Field(new FieldId(602), "HasRecentOrder", typeof(bool)))
            .Build()
            .Freeze()
            .CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "AccountEnabled", .95,
                EntityId: customer, FieldId: new FieldId(601), Value: "true"),
            new SemanticLexicalCandidate("active", SemanticLexicalCandidateKind.Field, "HasRecentOrder", .10,
                EntityId: customer, FieldId: new FieldId(602), Value: "true"));

        var decision = new SemanticLexicalResolver(model, source, candidateLimit: 1).Ground("active");

        Assert.Equal(GroundingOutcome.Committed, decision.Outcome);
        Assert.NotNull(decision.Committed);
        Assert.Equal("AccountEnabled", decision.Committed!.Steps[0].Candidate.CanonicalName);
        Assert.Empty(decision.EffectiveTruncationRisks);
    }

    private sealed class Dummy
    {
        public int Id { get; init; }
    }

    private sealed class FakeLexicalSource(params SemanticLexicalCandidate[] candidates)
        : ISemanticLexicalCandidateSource
    {
        public List<SemanticLexicalRequest> Requests { get; } = [];

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
        {
            Requests.Add(request);
            return candidates
                .Where(x => string.Equals(x.Token, request.Token, StringComparison.OrdinalIgnoreCase))
                .Where(x => request.EffectiveKinds.Contains(x.Kind))
                .OrderByDescending(x => x.Score)
                .Take(request.Limit)
                .ToArray();
        }
    }

    private sealed class SequencedSlowEmptyLexicalSource(params TimeSpan[] delays) : ISemanticLexicalCandidateSource
    {
        public List<SemanticLexicalRequest> Requests { get; } = [];

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
        {
            return RetrieveCore(request);
        }

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
            SemanticLexicalRequest request,
            CancellationToken cancellationToken)
        {
            return RetrieveCore(request);
        }

        private IReadOnlyList<SemanticLexicalCandidate> RetrieveCore(SemanticLexicalRequest request)
        {
            var index = Requests.Count;
            Requests.Add(request);
            var delay = delays[Math.Min(index, delays.Length - 1)];
            Thread.Sleep(delay);
            return [];
        }
    }

    private sealed class SlowEmptyLexicalSource(TimeSpan delay) : ISemanticLexicalCandidateSource
    {
        public List<SemanticLexicalRequest> Requests { get; } = [];

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
        {
            Requests.Add(request);
            Thread.Sleep(delay);
            return [];
        }

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
            SemanticLexicalRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Thread.Sleep(delay);
            return [];
        }
    }

    /// <summary>Simulates a slow/hung retrieval provider (e.g. a network
    /// partition or a slow index) so retrieval-timeout behavior can be
    /// exercised deterministically.</summary>
    private sealed class SlowLexicalSource(TimeSpan delay) : ISemanticLexicalCandidateSource
    {
        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
        {
            Thread.Sleep(delay);
            return [];
        }

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request,
            CancellationToken cancellationToken)
        {
            Thread.Sleep(delay);
            return [];
        }
    }
}