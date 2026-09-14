using System.Threading;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Grounding;

/// <summary>
/// Case study for the "Complexity bounds" section of docs/LEXICAL-GROUNDING.md,
/// run against the real generated Supply Chain semantic contract instead of
/// the toy model used in the core library's
/// <c>SemanticLexicalResolverTests</c>. Every control in that doc's table
/// (<c>maxTokens</c>, <c>maxPathsExplored</c>, <c>timeout</c>,
/// <c>retrievalTimeout</c>, cancellation) fails closed the same way:
/// <see cref="GroundingOutcome.BudgetExceeded"/> with <c>Committed = null</c>,
/// never a best-effort answer built from a search that was cut off before it
/// could prove there was only one legal interpretation.
/// </summary>
public sealed class SupplyChainGroundingBudgetTests
{
    [Fact]
    public void Ground_reports_budget_exceeded_when_expression_exceeds_max_tokens()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "suppliers", SemanticLexicalCandidateKind.Entity, "Supplier", .95,
                EntityId: SupplyChainSemanticModel.Supplier));

        // maxTokens: 3 — the five-token expression must be refused before any
        // retrieval or search runs, not silently truncated or best-effort
        // resolved from the first three tokens.
        var resolver = new SemanticLexicalResolver(contract, source, maxTokens: 3);
        var decision = resolver.Ground("show me all active suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.MaxTokens, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
        Assert.Empty(source.Requests); // no retrieval happened — refused up front
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_search_exceeds_max_paths_explored()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "suppliers", SemanticLexicalCandidateKind.Entity, "Supplier", .90,
                EntityId: SupplyChainSemanticModel.Supplier),
            new SemanticLexicalCandidate(
                "active", SemanticLexicalCandidateKind.Value, "PurchaseOrder.Status = Open", .90,
                EntityId: SupplyChainSemanticModel.PurchaseOrder,
                FieldId: SupplyChainSemanticModel.Field("PurchaseOrder", "Status"),
                Value: "Open"));

        // maxPathsExplored: 1 — the search must stop and fail closed rather
        // than continue past the configured work ceiling.
        var resolver = new SemanticLexicalResolver(contract, source, maxPathsExplored: 1);
        var decision = resolver.Ground("active suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.MaxPathsExplored, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_on_timeout()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "suppliers", SemanticLexicalCandidateKind.Entity, "Supplier", .95,
                EntityId: SupplyChainSemanticModel.Supplier));

        // A one-tick timeout is effectively already elapsed by the time the
        // search's first budget check runs, so this is deterministic rather
        // than a flaky wall-clock race.
        var resolver = new SemanticLexicalResolver(contract, source, timeout: TimeSpan.FromTicks(1));
        var decision = resolver.Ground("suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.Timeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_cancellation_is_requested()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "suppliers", SemanticLexicalCandidateKind.Entity, "Supplier", .90,
                EntityId: SupplyChainSemanticModel.Supplier),
            new SemanticLexicalCandidate(
                "active", SemanticLexicalCandidateKind.Value, "PurchaseOrder.Status = Open", .90,
                EntityId: SupplyChainSemanticModel.PurchaseOrder,
                FieldId: SupplyChainSemanticModel.Field("PurchaseOrder", "Status"),
                Value: "Open"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var resolver = new SemanticLexicalResolver(contract, source);
        var decision = resolver.Ground("active suppliers", cts.Token);

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.Cancelled, decision.BudgetLimit);
        Assert.Null(decision.Committed);

        // Resolve() must surface the same fail-closed signal, not "resolved
        // to whatever was found before cancellation."
        var resolved = new SemanticLexicalResolver(contract, source).Resolve("active suppliers", cts.Token);
        Assert.Equal(SemanticLexicalResolutionOutcome.BudgetExceeded, resolved.Outcome);
    }

    [Fact]
    public void Ground_reports_budget_exceeded_when_retrieval_exceeds_retrieval_timeout()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        // A candidate source that simulates a slow/hung provider (network
        // partition, slow index). retrievalTimeout is checked before each
        // token's Retrieve call, so a short-enough timeout deterministically
        // fires on the very first token without needing to race the sleep.
        var source = new SlowLexicalSource(TimeSpan.FromMilliseconds(20));
        var resolver = new SemanticLexicalResolver(contract, source, retrievalTimeout: TimeSpan.FromTicks(1));

        var decision = resolver.Ground("suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
    }

    [Fact]
    public void Ground_exposes_partial_interpretations_on_budget_exceeded_but_never_commits_them()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var source = new FakeLexicalSource(
            new SemanticLexicalCandidate(
                "suppliers", SemanticLexicalCandidateKind.Entity, "Supplier", .90,
                EntityId: SupplyChainSemanticModel.Supplier),
            new SemanticLexicalCandidate(
                "active", SemanticLexicalCandidateKind.Value, "PurchaseOrder.Status = Open", .90,
                EntityId: SupplyChainSemanticModel.PurchaseOrder,
                FieldId: SupplyChainSemanticModel.Field("PurchaseOrder", "Status"),
                Value: "Open"));

        // Generous enough to let at least one full path complete before the
        // budget trips on the next unit of work, so
        // PartialInterpretationsAtCutoff is exercised rather than always
        // empty.
        var resolver = new SemanticLexicalResolver(contract, source, maxPathsExplored: 3);
        var decision = resolver.Ground("active suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Null(decision.Committed);
        Assert.Empty(decision.CompetingInterpretations);
        // Diagnostic-only: whatever was found is inspectable, but never
        // treated as authorizable — Committed stays null above regardless of
        // how many partial interpretations were captured.
        Assert.NotNull(decision.EffectivePartialInterpretationsAtCutoff);
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
                .ToArray();
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