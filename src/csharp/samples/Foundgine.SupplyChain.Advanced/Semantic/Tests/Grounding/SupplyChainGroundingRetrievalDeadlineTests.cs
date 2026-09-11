using System.Threading;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Grounding;

/// <summary>
/// Supply Chain case studies for bounded lexical retrieval. These scenarios
/// deliberately use the real generated Supply Chain semantic contract so the
/// sample demonstrates the same fail-closed retrieval guarantees as the core
/// resolver tests: a retrieval deadline is one shared budget across every
/// token lookup and any compact-token fallback.
/// </summary>
public sealed class SupplyChainGroundingRetrievalDeadlineTests
{
    [Fact]
    public void Ground_fails_closed_when_retrieval_deadline_is_already_exhausted_before_provider_call()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var source = new RecordingSlowLexicalSource(TimeSpan.FromMilliseconds(20));
        var resolver = new SemanticLexicalResolver(
            contract, source, retrievalTimeout: TimeSpan.FromTicks(1));

        var decision = resolver.Ground("suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Empty(source.Requests);
    }

    [Fact]
    public void Ground_detects_retrieval_timeout_after_slow_provider_returns()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();
        var source = new RecordingSlowLexicalSource(TimeSpan.FromMilliseconds(20));
        var resolver = new SemanticLexicalResolver(
            contract, source, retrievalTimeout: TimeSpan.FromMilliseconds(5));

        var decision = resolver.Ground("suppliers");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Single(source.Requests);
        Assert.Equal("suppliers", source.Requests[0].Token);
    }

    [Fact]
    public void Ground_uses_one_retrieval_deadline_across_compact_token_fallback()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        // The first token lookup completes inside the overall budget. It
        // returns no candidates, which causes the resolver to make exactly
        // one compact-token fallback lookup. That fallback deliberately takes
        // longer than the remaining shared budget. A fresh timeout for the
        // fallback would incorrectly permit it to run for another full budget.
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
        var resolver = new SemanticLexicalResolver(
            contract, source, retrievalTimeout: TimeSpan.FromMilliseconds(200));

        var decision = resolver.Ground("purchase order");

        Assert.Equal(GroundingOutcome.BudgetExceeded, decision.Outcome);
        Assert.Equal(GroundingBudgetLimit.RetrievalTimeout, decision.BudgetLimit);
        Assert.Null(decision.Committed);
        Assert.Equal(2, source.Requests.Count);
        Assert.Contains(source.Requests[0].Token, new[] { "purchase", "order" });
        Assert.Equal("purchaseorder", source.Requests[1].Token);
    }

    private sealed class RecordingSlowLexicalSource(TimeSpan delay) : ISemanticLexicalCandidateSource
    {
        public List<SemanticLexicalRequest> Requests { get; } = [];

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
        {
            Requests.Add(request);
            Thread.Sleep(delay);
            return [];
        }

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
            SemanticLexicalRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Thread.Sleep(delay);
            return [];
        }
    }

    private sealed class SequencedSlowEmptyLexicalSource(params TimeSpan[] delays)
        : ISemanticLexicalCandidateSource
    {
        public List<SemanticLexicalRequest> Requests { get; } = [];

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
            => RetrieveCore(request);

        public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
            SemanticLexicalRequest request, CancellationToken cancellationToken)
            => RetrieveCore(request);

        private IReadOnlyList<SemanticLexicalCandidate> RetrieveCore(
            SemanticLexicalRequest request)
        {
            var index = Requests.Count;
            Requests.Add(request);
            Thread.Sleep(delays[Math.Min(index, delays.Length - 1)]);
            return [];
        }
    }
}