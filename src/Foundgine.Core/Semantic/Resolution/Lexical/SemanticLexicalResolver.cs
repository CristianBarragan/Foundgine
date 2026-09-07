using Foundgine.Core.Abstractions;
using System.Threading;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Resolves free-form lexical tokens by first generating candidates across
/// semantic kinds and then selecting a high-scoring path constrained by the
/// frozen semantic graph. Retrieval scores order hypotheses; graph topology
/// determines whether a hypothesis is legal.
/// </summary>
public sealed class SemanticLexicalResolver
{
    private readonly SemanticContractSnapshot _contract;
    private readonly ISemanticLexicalCandidateSource _source;
    private readonly int _candidateLimit;
    private readonly int _maxBridgeHops;
    private readonly double _ambiguityThreshold;
    private readonly int _maxTokens;
    private readonly int _maxPathsExplored;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retrievalTimeout;
    private readonly int? _minimumAliasWeight;

    /// <param name="contract">The frozen semantic contract to resolve against.</param>
    /// <param name="source">The candidate retrieval boundary (Elasticsearch, pgvector, or a composite).</param>
    /// <param name="candidateLimit">Maximum candidates retained per token across all semantic kinds. The
    /// candidate source is queried once per token with a one-candidate sentinel (<c>candidateLimit + 1</c>)
    /// so the resolver can reliably observe provider-side truncation. The returned candidates are normalized
    /// and then truncated to this total limit before graph search.</param>
    /// <param name="maxBridgeHops">Maximum relationship hops the bridging BFS will traverse to connect a
    /// candidate back to the current entity. Bounds graph depth per transition.</param>
    /// <param name="ambiguityThreshold">Interpretation-score gap below which two competing interpretations
    /// are considered within the ambiguity margin and force <see cref="GroundingOutcome.RequiresClarification"/>.</param>
    /// <param name="maxTokens">Maximum tokens an expression may contain. Checked before any retrieval or
    /// search runs, since token count is the dominant term in the search's worst-case branching
    /// (<c>candidateLimit ^ tokenCount</c>, before graph-legality pruning). Expressions over this limit
    /// return <see cref="GroundingOutcome.BudgetExceeded"/> immediately.</param>
    /// <param name="maxPathsExplored">Maximum total search-work units (DFS node expansions plus bridging
    /// BFS dequeues, combined) across one see cref="Ground" call. This is the primary defence against
    /// the combinatorial blow-up of <c>tokens times; semantic kinds times; candidates times; paths
    /// times; backtracking</c> — it bounds total work regardless of how permissive the candidate source
    /// or how connected the graph is. Backtracking branches are not budgeted separately: every backtrack
    /// is a DFS re-entry and consumes this same shared limit.</param>
    /// <param name="timeout">Wall-clock ceiling for the in-memory graph search portion of one
    /// see cref="Ground" call, independent of the node-count budget. This clock starts only after
    /// candidate retrieval has completed; see <paramref name="retrievalTimeout"/> for the stage before it.
    /// Defaults to 250ms; pass a longer value for large contracts.</param>
    /// <param name="minimumAliasWeight">Optional minimum declared alias weight required to commit an
    /// interpretation. When null, weighted aliases remain diagnostic evidence only. When configured,
    /// retrieval scores still rank interpretations, but a uniquely selected interpretation containing
    /// a declared alias below this threshold cannot be committed and returns
    /// <see cref="GroundingOutcome.RequiresClarification"/>. This is a commitment policy, not an
    /// authorization decision.</param>
    /// <param name="retrievalTimeout">Wall-clock ceiling for candidate retrieval across all tokens in one
    /// <see>
    ///     <cref>Ground</cref>
    /// </see>
    ///     call. Retrieval (Elasticsearch, pgvector, or any I/O-backed
    /// <see cref="ISemanticLexicalCandidateSource"/>) happens entirely before the in-memory search budget
    /// starts counting, so without this bound a slow or hung candidate source could block <see>
    ///         <cref>Ground</cref>
    ///     </see>
    ///     indefinitely regardless of how aggressively <paramref name="maxPathsExplored"/> or <paramref name="timeout"/>
    /// are configured. Defaults to 2 seconds — looser than the in-memory search timeout, since retrieval
    /// legitimately involves network or database I/O.</param>
    /// <remarks>
    /// Every limit here fails closed: hitting <paramref name="maxTokens"/>, <paramref name="maxPathsExplored"/>,
    /// <paramref name="timeout"/>, <paramref name="retrievalTimeout"/>, or a cancelled
    /// <see cref="CancellationToken"/> produces <see cref="GroundingOutcome.BudgetExceeded"/> with
    /// <c>Committed = null</c>, never a best-effort interpretation from a search that could not prove it was
    /// the only legal one. Whatever interpretations a search-time limit had already constructed are still
    /// exposed diagnostically via <see cref="GroundingDecision.PartialInterpretationsAtCutoff"/> — for
    /// logging and budget tuning only, never for execution.
    /// </remarks>
    public SemanticLexicalResolver(
        SemanticContractSnapshot contract,
        ISemanticLexicalCandidateSource source,
        int candidateLimit = 20,
        int maxBridgeHops = 4,
        double ambiguityThreshold = 0.03d,
        int maxTokens = 32,
        int maxPathsExplored = 5000,
        TimeSpan? timeout = null,
        TimeSpan? retrievalTimeout = null,
        int? minimumAliasWeight = null)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (candidateLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(candidateLimit));
        if (maxBridgeHops is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(maxBridgeHops));
        if (ambiguityThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(ambiguityThreshold));
        if (maxTokens is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        if (maxPathsExplored is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maxPathsExplored));
        if (timeout is { } t && t <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (retrievalTimeout is { } rt && rt <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retrievalTimeout));
        if (minimumAliasWeight is { } maw && maw is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumAliasWeight));
        _candidateLimit = candidateLimit;
        _maxBridgeHops = maxBridgeHops;
        _ambiguityThreshold = ambiguityThreshold;
        _maxTokens = maxTokens;
        _maxPathsExplored = maxPathsExplored;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(250);
        _retrievalTimeout = retrievalTimeout ?? TimeSpan.FromSeconds(2);
        _minimumAliasWeight = minimumAliasWeight;
    }

    /// <summary>
    /// Resolves an expression to its single best-scoring semantic path.
    /// Kept for callers that only need "the answer" and are prepared to treat
    /// <see cref="SemanticLexicalResolutionOutcome.Ambiguous"/> as a signal to
    /// stop. Callers that need to inspect *why* it was ambiguous, or what the
    /// competing meanings actually were, should call <see>
    ///     <cref>Ground</cref>
    /// </see>
    /// instead —
    /// this method only tells you a tie existed, not what it was between.
    /// </summary>
    public SemanticLexicalResolution Resolve(string expression, CancellationToken cancellationToken = default)
    {
        var decision = Ground(expression, cancellationToken);

        if (decision.Outcome == GroundingOutcome.Unresolved)
            return new(SemanticLexicalResolutionOutcome.Unresolved, [], 0, null, decision.Reason,
                decision.RootCandidates);

        if (decision.Outcome == GroundingOutcome.BudgetExceeded)
            return new(SemanticLexicalResolutionOutcome.BudgetExceeded, [], 0, null, decision.Reason,
                decision.RootCandidates);

        var leading = decision.Committed ?? decision.CompetingInterpretations[0];
        var outcome = decision.Outcome == GroundingOutcome.RequiresClarification
            ? SemanticLexicalResolutionOutcome.Ambiguous
            : SemanticLexicalResolutionOutcome.Resolved;

        return new(
            outcome,
            leading.Steps,
            leading.InterpretationScore,
            leading.RootEntity,
            decision.Reason,
            decision.RootCandidates);
    }

    /// <summary>
    /// Grounds an expression against the semantic contract and returns every
    /// structurally valid, semantically distinct interpretation — not just the
    /// top-ranked one. Interpretations that reach the same relationship, field,
    /// or value via different bridging routes are treated as one meaning (the
    /// route is a retrieval/graph artifact, not part of what the user meant).
    /// Interpretations that map a token onto a different field, value,
    /// relationship, or root entity are treated as competing meanings: if two
    /// or more of those remain within <c>ambiguityThreshold</c> interpretation-score
    /// separation of each other, Foundgine reports <see cref="GroundingOutcome.RequiresClarification"/>
    /// instead of committing to whichever one happened to score highest.
    /// </summary>
    public GroundingDecision Ground(string expression) => Ground(expression, CancellationToken.None);

    public GroundingDecision Ground(string expression, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Lexical expression cannot be empty.", nameof(expression));

        var tokens = Tokenize(expression);
        if (tokens.Length == 0)
            return new(expression, GroundingOutcome.Unresolved, null, [], "No lexical tokens were found.", []);

        // Token count is the dominant term in the search's worst-case branching
        // (candidateLimit ^ tokenCount before graph-legality pruning), so it is
        // checked before any retrieval or search work is done at all.
        if (tokens.Length > _maxTokens)
            return new(
                expression,
                GroundingOutcome.BudgetExceeded,
                null,
                [],
                $"Expression has {tokens.Length} tokens, exceeding the configured maximum of {_maxTokens}. Grounding was refused before any retrieval or graph search ran.",
                [],
                GroundingBudgetLimit.MaxTokens);

        IReadOnlyDictionary<string, IReadOnlyList<SemanticLexicalCandidate>> candidateSets;
        var truncations = new Dictionary<string, CandidateTruncation>(StringComparer.OrdinalIgnoreCase);
        using var retrievalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        retrievalCts.CancelAfter(_retrievalTimeout);
        var retrievalStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            candidateSets = GetCandidates(tokens, retrievalCts.Token, retrievalStopwatch, stopOnFirstEmpty: true,
                truncations: truncations);
        }
        catch (GroundingRetrievalTimeoutException ex)
        {
            return new(
                expression,
                GroundingOutcome.BudgetExceeded,
                null,
                [],
                $"Candidate retrieval for token '{ex.Token}' exceeded the configured retrieval timeout ({ex.Elapsed.TotalMilliseconds:0}ms). Grounding was refused before the graph search began.",
                [],
                GroundingBudgetLimit.RetrievalTimeout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                expression,
                GroundingOutcome.BudgetExceeded,
                null,
                [],
                "Candidate retrieval was cancelled before the graph search began.",
                [],
                GroundingBudgetLimit.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return new(
                expression,
                GroundingOutcome.BudgetExceeded,
                null,
                [],
                $"Candidate retrieval exceeded the configured retrieval timeout ({retrievalStopwatch.Elapsed.TotalMilliseconds:0}ms). Grounding was refused before the graph search began.",
                [],
                GroundingBudgetLimit.RetrievalTimeout);
        }

        if (candidateSets.Values.Any(x => x.Count == 0))
        {
            // Semantic declaration names are commonly PascalCase identifiers
            // (for example PurchaseOrder) while callers naturally write the
            // same name with spaces ("purchase order"). If token-by-token
            // retrieval cannot ground the expression, make one bounded fallback
            // lookup for the compact spelling. This is deliberately all-or-
            // nothing: it does not silently discard unknown filler tokens or
            // invent arbitrary n-gram segmentations.
            if (tokens.Length > 1)
            {
                var compactToken = string.Concat(tokens);
                try
                {
                    var compactTruncations = new Dictionary<string, CandidateTruncation>(StringComparer.OrdinalIgnoreCase);
                    var compactCandidates = GetCandidates([compactToken], retrievalCts.Token, retrievalStopwatch,
                        truncations: compactTruncations);
                    if (compactCandidates[compactToken].Count > 0)
                    {
                        tokens = [compactToken];
                        candidateSets = compactCandidates;
                        truncations = compactTruncations;
                    }
                }
                catch (GroundingRetrievalTimeoutException ex)
                {
                    return new(
                        expression,
                        GroundingOutcome.BudgetExceeded,
                        null,
                        [],
                        $"Candidate retrieval for compact token '{ex.Token}' exceeded the configured retrieval timeout ({ex.Elapsed.TotalMilliseconds:0}ms). Grounding was refused before the graph search began.",
                        [],
                        GroundingBudgetLimit.RetrievalTimeout);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new(
                        expression,
                        GroundingOutcome.BudgetExceeded,
                        null,
                        [],
                        "Candidate retrieval was cancelled before the graph search began.",
                        [],
                        GroundingBudgetLimit.Cancelled);
                }
                catch (OperationCanceledException)
                {
                    return new(
                        expression,
                        GroundingOutcome.BudgetExceeded,
                        null,
                        [],
                        $"Candidate retrieval exceeded the configured retrieval timeout ({retrievalStopwatch.Elapsed.TotalMilliseconds:0}ms). Grounding was refused before the graph search began.",
                        [],
                        GroundingBudgetLimit.RetrievalTimeout);
                }
            }

            if (candidateSets.Values.Any(x => x.Count == 0) || tokens.Any(x => !candidateSets.ContainsKey(x)))
            {
                // With stopOnFirstEmpty, a failed compact-token fallback can
                // leave candidateSets holding only the tokens queried before
                // the empty one that triggered the fallback attempt — later
                // tokens were never looked up at all. Treat an unqueried
                // token the same as an empty one rather than indexing the
                // dictionary directly, which would throw for those tokens.
                var missing = tokens.First(x => !candidateSets.TryGetValue(x, out var c) || c.Count == 0);
                return new(
                    expression,
                    GroundingOutcome.Unresolved,
                    null,
                    [],
                    $"No lexical candidate was returned for token '{missing}'.",
                    []);
            }
        }

        var roots = candidateSets[tokens[0]]
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var budget = new SearchBudget(_maxPathsExplored, _timeout, cancellationToken);
        var rawPaths = new List<SemanticLexicalResolution>();
        foreach (var root in roots)
        {
            if (budget.Exceeded) break;

            var state = CreateRootState(tokens[0], root);
            if (state is null)
                continue;

            Search(tokens, 1, candidateSets, state, rawPaths, budget);
        }

        // Fail closed: a search the budget cut short cannot prove it found
        // every legal interpretation, so whatever partial results exist are
        // not trustworthy evidence of a single meaning. Never fall through
        // to authorizing the best candidate found before the limit was hit.
        // The partial interpretations found so far are still surfaced, but
        // strictly as a diagnostic — Committed remains null regardless.
        if (budget.Exceeded)
        {
            var partial = rawPaths
                .Select(p => CreateInterpretation(p))
                .GroupBy(x => x.Signature)
                .Select(g => g.OrderByDescending(x => x.InterpretationScore).First())
                .OrderByDescending(x => x.InterpretationScore)
                .ToArray();

            return new(
                expression,
                GroundingOutcome.BudgetExceeded,
                null,
                [],
                $"Grounding stopped ({budget.LimitHit}) before every candidate interpretation could be explored. " +
                $"{partial.Length} partial interpretation(s) had been found at cutoff; none is proof of a unique " +
                "legal meaning, so nothing was committed.",
                roots,
                budget.LimitHit,
                partial);
        }

        if (rawPaths.Count == 0)
            return new(
                expression,
                GroundingOutcome.Unresolved,
                null,
                [],
                "No complete semantic path could be constructed from the lexical candidates.",
                roots);

        // Collapse paths that agree on what each token maps onto but disagree
        // only on which graph route connects them. Those are not competing
        // meanings; they are alternate evidence for the same interpretation,
        // so surfacing them as "ambiguity" would just be retrieval noise.
        var interpretations = rawPaths
            .Select(p => CreateInterpretation(p))
            .GroupBy(x => x.Signature)
            .Select(g => g.OrderByDescending(x => x.InterpretationScore).First())
            .OrderByDescending(x => x.InterpretationScore)
            .ThenBy(x => x.RootEntity.Value)
            .ToArray();

        var best = interpretations[0];

        // Retrieval/graph score answers "which interpretation ranks first".
        // Alias weight answers a different policy question: "is that selected
        // meaning sufficiently supported by application-declared lexical
        // evidence to commit?" Weight never changes the ranking itself.
        if (_minimumAliasWeight is { } minimumAliasWeight &&
            best.EffectiveAliasEvidence.Status == AliasEvidenceStatus.Insufficient)
        {
            return new(
                expression,
                GroundingOutcome.RequiresClarification,
                null,
                [],
                $"The leading interpretation contains declared lexical alias evidence below the configured minimum weight of {minimumAliasWeight}. Foundgine will not commit the interpretation automatically.",
                roots,
                GroundingBudgetLimit.None,
                null,
                best.EffectiveAliasEvidence);
        }

        var withinAmbiguityMargin = interpretations
            .Where(x => Math.Abs(x.InterpretationScore - best.InterpretationScore) < _ambiguityThreshold)
            .ToArray();

        if (withinAmbiguityMargin.Length <= 1)
        {
            // Graph search found no competing meaning — but graph search only
            // ever saw the candidates that survived retrieval-time truncation.
            // If a token this interpretation depends on had a candidate cut
            // within the ambiguity margin of what was kept, apply the same
            // "do not silently pick a winner inside the margin" policy here,
            // one stage earlier than usual: the cut candidate was never
            // proven illegal, it just never got to try.
            var truncationRisk = best.Steps
                .Select(s => s.Token)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => truncations.TryGetValue(t, out var truncation) ? truncation : null)
                .Where(t => t is not null && t.WithinAmbiguityMargin(_ambiguityThreshold))
                .Select(t => t!)
                .ToArray();

            if (truncationRisk.Length > 0)
            {
                var riskyTokens = string.Join(", ", truncationRisk.Select(t => $"'{t.Token}'"));
                return new(
                    expression,
                    GroundingOutcome.RequiresClarification,
                    null,
                    interpretations.Skip(1).ToArray(),
                    $"The leading interpretation depends on token(s) {riskyTokens} whose candidate set was cut " +
                    $"down to the configured candidate limit, and the highest-scoring candidate that was cut " +
                    $"fell within {_ambiguityThreshold:P0} of the lowest one kept. That candidate was never " +
                    "checked against the semantic graph, so it cannot be ruled out as a distinct legal meaning. " +
                    "Foundgine will not commit automatically; raise the candidate limit or inspect TruncationRisks.",
                    roots,
                    GroundingBudgetLimit.None,
                    null,
                    best.EffectiveAliasEvidence,
                    truncationRisk);
            }

            return new(
                expression,
                GroundingOutcome.Committed,
                best,
                interpretations.Skip(1).ToArray(),
                interpretations.Length > 1
                    ? "One interpretation scored clearly above the others; the remaining interpretations are retained as rejected alternatives, not discarded."
                    : "A single semantic interpretation was constructed and no competing meaning was found.",
                roots,
                GroundingBudgetLimit.None,
                null,
                best.EffectiveAliasEvidence);
        }

        return new(
            expression,
            GroundingOutcome.RequiresClarification,
            null,
            withinAmbiguityMargin,
            $"{withinAmbiguityMargin.Length} interpretations map these tokens onto different fields, values, relationships, or entities and are within {_ambiguityThreshold:P0} interpretation-score separation of each other. Committing to the top-ranked one would risk a perfectly authorized misunderstanding.",
            roots,
            GroundingBudgetLimit.None,
            null,
            best.EffectiveAliasEvidence);
    }

    private GroundingInterpretation CreateInterpretation(SemanticLexicalResolution resolution)
    {
        var aliasEvidence = AliasWeightEvidenceGate.Evaluate(
            _contract,
            _minimumAliasWeight ?? 1,
            resolution);

        return new GroundingInterpretation(
            resolution.Steps,
            resolution.Confidence,
            resolution.RootEntity!.Value,
            Signature(resolution.Steps),
            AliasInterpretationEvidence.From(aliasEvidence));
    }

    /// <summary>Identifies what an interpretation means — the ordered mapping
    /// to canonical contract identities — independent of caller wording and of
    /// which bridging route the graph search used to get there. Aliases and
    /// canonical spellings must therefore produce the same signature. Two paths
    /// with the same signature are the same interpretation.</summary>
    private static string Signature(IReadOnlyList<SemanticLexicalStep> steps) =>
        string.Join(
            "|",
            steps.Select(s =>
                $"{MeaningKind(s.Candidate.Kind)}:{s.Candidate.CanonicalName}:{s.Candidate.EntityId}:{s.Candidate.FieldId}:{s.Candidate.RelationshipId}:{s.Candidate.Value}"));

    /// <summary>Returns the semantic identity represented by a retrieval
    /// candidate. Entity and Node are intentionally the same identity because
    /// the lexicon contains both a semantic entity document and a graph-node
    /// document for the same declared entity. The remaining identity fields
    /// distinguish genuinely different meanings.</summary>
    private static string SemanticIdentityKey(SemanticLexicalCandidate candidate) =>
        $"{MeaningKind(candidate.Kind)}:{candidate.CanonicalName}:{candidate.EntityId}:{candidate.FieldId}:{candidate.RelationshipId}:{candidate.Value}";

    private static int CandidateKindPreference(SemanticLexicalCandidateKind kind) =>
        kind switch
        {
            SemanticLexicalCandidateKind.Entity => 0,
            SemanticLexicalCandidateKind.Node => 1,
            _ => 2
        };

    /// <summary>
    /// Normalizes retrieval-only representations that carry the same semantic
    /// identity. An Entity lexicon document and its graph Node document are two
    /// ways to retrieve the same declared entity, not two competing meanings.
    /// Keeping that implementation detail in the ambiguity signature makes an
    /// exact canonical name (and every alias of it) spuriously ambiguous.
    /// </summary>
    private static SemanticLexicalCandidateKind MeaningKind(SemanticLexicalCandidateKind kind) =>
        kind == SemanticLexicalCandidateKind.Node
            ? SemanticLexicalCandidateKind.Entity
            : kind;

    /// <summary>Returns the ranked candidate matrix used by the resolver.
    /// Each token is queried once across all semantic kinds.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SemanticLexicalCandidate>> GetCandidates(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Lexical expression cannot be empty.", nameof(expression));

        using var retrievalCts = new CancellationTokenSource(_retrievalTimeout);
        return GetCandidates(Tokenize(expression), retrievalCts.Token, System.Diagnostics.Stopwatch.StartNew());
    }

    /// <summary>Retrieves candidates for every token, bounded by
    /// <see cref="_retrievalTimeout"/> and <paramref name="cancellationToken"/>.
    /// This runs entirely before the in-memory search's <see cref="SearchBudget"/>
    /// is constructed, so it needs its own independent bound — otherwise a slow
    /// or hung candidate source could block <see>
    ///     <cref>Ground</cref>
    /// </see>
    /// indefinitely regardless of how the search-time limits are configured. The resolver shares one
    /// retrieval deadline across all token and compact-fallback lookups.</summary>
    /// <param name="tokens">The lexical tokens to retrieve candidates for.</param>
    /// <param name="cancellationToken">Token observed between and after individual retrieval calls.</param>
    /// <param name="stopwatch">The shared clock the retrieval deadline is measured against.</param>
    /// <param name="stopOnFirstEmpty">
    /// When <c>true</c>, retrieval stops as soon as any token comes back with
    /// no candidates instead of continuing through the remaining tokens. The
    /// caller (the initial per-token pass in <see>
    ///     <cref>Ground</cref>
    /// </see>
    /// ) falls back to a single compact-token lookup whenever *any* token is
    /// empty, so querying the rest of the tokens individually first would
    /// only spend shared retrieval budget on results that are about to be
    /// discarded — budget the compact-token fallback needs instead.
    /// </param>
    /// <param name="truncations">When non-null, populated with one <see cref="CandidateTruncation"/>
    /// entry per token whose retrieved candidate set exceeded <c>candidateLimit</c> and was cut down to
    /// it. Diagnostic only — recorded here because this is the one place a truncated candidate is still
    /// visible before it is discarded; callers that don't need the diagnostic can leave this null.</param>
    private IReadOnlyDictionary<string, IReadOnlyList<SemanticLexicalCandidate>> GetCandidates(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken,
        System.Diagnostics.Stopwatch stopwatch,
        bool stopOnFirstEmpty = false,
        Dictionary<string, CandidateTruncation>? truncations = null)
    {
        var result = new Dictionary<string, IReadOnlyList<SemanticLexicalCandidate>>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            if (stopwatch.Elapsed >= _retrievalTimeout)
                throw new GroundingRetrievalTimeoutException(token, stopwatch.Elapsed);

            cancellationToken.ThrowIfCancellationRequested();

            // Ask for one more candidate than we are willing to retain. The source is
            // allowed (and expected) to honor this limit, so requesting exactly
            // _candidateLimit would make provider-side truncation indistinguishable
            // from an exact result set of that size. The extra row is a sentinel: if
            // it arrives, we have proof that at least one candidate was cut at the
            // resolver boundary.
            var retrievalLimit = _candidateLimit + 1;
            var retrieved = _source.Retrieve(
                new SemanticLexicalRequest(token, Limit: retrievalLimit),
                cancellationToken);

            // A synchronous provider may ignore the cancellation token. Detect
            // that case at the same deadline boundary immediately after it returns.
            if (stopwatch.Elapsed >= _retrievalTimeout)
                throw new GroundingRetrievalTimeoutException(token, stopwatch.Elapsed);

            // Preserve the raw count before normalization/deduplication. A source
            // may legitimately return multiple retrieval representations of the same
            // semantic identity; deduplicating first could erase the sentinel and make
            // a genuinely truncated source appear complete.
            var wasTruncated = retrieved.Count > _candidateLimit;

            var candidates = retrieved
                .Where(x => x.Score >= 0)
                // Retrieval providers may legitimately index the same declared
                // entity more than once (for example the semantic Entity and
                // its graph Node projection). Those are two retrieval
                // representations of one meaning, not two meanings. Collapse
                // them before graph search so duplicate index documents can
                // never turn into a false clarification result. Deliberately
                // do NOT collapse different semantic kinds (field,
                // relationship, value, or a different entity), because those
                // remain genuine ambiguity.
                .GroupBy(SemanticIdentityKey)
                .Select(g => g
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => CandidateKindPreference(x.Kind))
                    .ThenBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderByDescending(x => x.Score)
                .ThenBy(x => CandidateKindPreference(x.Kind))
                .ThenBy(x => x.CanonicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Record truncation before the retained set is handed to graph search.
            // Because retrieval used a one-candidate sentinel, this remains observable
            // even when the provider correctly enforces the requested Limit.
            if (truncations is not null && wasTruncated && candidates.Length >= _candidateLimit)
            {
                var lowestRetainedScore = candidates[_candidateLimit - 1].Score;
                var highestTruncatedScore = candidates.Length > _candidateLimit
                    ? candidates[_candidateLimit].Score
                    : double.NegativeInfinity;
                truncations[token] = new CandidateTruncation(
                    token,
                    RetainedCount: _candidateLimit,
                    TruncatedCount: Math.Max(1, retrieved.Count - _candidateLimit),
                    LowestRetainedScore: lowestRetainedScore,
                    HighestTruncatedScore: highestTruncatedScore);
            }

            candidates = candidates.Take(_candidateLimit).ToArray();

            // A bare exact canonical entity name is an entity-root expression.
            // Relationship members can legitimately share that spelling (for
            // example PurchaseOrder.supplier and PurchaseOrderLine.purchaseOrder),
            // but they must not make the canonical entity root ambiguous. Aliases
            // remain ambiguity-aware and are not affected by this preference.
            var exactEntityRoots = candidates
                .Where(x =>
                    (x.Kind is SemanticLexicalCandidateKind.Entity or SemanticLexicalCandidateKind.Node) &&
                    string.Equals(x.CanonicalName, token, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exactEntityRoots.Length > 0)
                candidates = exactEntityRoots;

            result[token] = candidates;

            if (stopOnFirstEmpty && candidates.Length == 0)
                break;
        }

        return result;
    }

    private void Search(
        IReadOnlyList<string> tokens,
        int index,
        IReadOnlyDictionary<string, IReadOnlyList<SemanticLexicalCandidate>> candidates,
        SearchState state,
        List<SemanticLexicalResolution> results,
        SearchBudget budget)
    {
        if (budget.Tick()) return;

        if (index == tokens.Count)
        {
            var interpretationScore = state.Steps.Count == 0
                ? 0
                : state.Steps.Select(x => x.Candidate.Score).Average() * state.GraphFactor;
            results.Add(new(
                SemanticLexicalResolutionOutcome.Resolved,
                state.Steps,
                Math.Clamp(interpretationScore, 0, 1),
                state.RootEntity,
                "A complete lexical path was grounded against the semantic contract."));
            return;
        }

        var token = tokens[index];
        foreach (var candidate in candidates[token].Take(_candidateLimit))
        {
            if (budget.Tick()) return;

            foreach (var transition in ResolveTransition(state, candidate, budget))
            {
                if (budget.Tick()) return;

                var next = state.Add(
                    new SemanticLexicalStep(token, candidate, transition.Factor, transition.BridgingPath),
                    transition.Factor);
                Search(tokens, index + 1, candidates, next, results, budget);

                if (budget.Exceeded) return;
            }
        }
    }

    private SearchState? CreateRootState(string token, SemanticLexicalCandidate candidate)
    {
        return candidate.Kind switch
        {
            SemanticLexicalCandidateKind.Entity or SemanticLexicalCandidateKind.Node
                when candidate.EntityId is not null && _contract.TryGet(candidate.EntityId.Value, out _) =>
                new(candidate.EntityId.Value, candidate.EntityId.Value,
                    [new SemanticLexicalStep(token, candidate, candidate.Score, [])], candidate.Score),

            SemanticLexicalCandidateKind.Relationship
                when candidate.SourceEntityId is not null && candidate.TargetEntityId is not null &&
                     _contract.TryGet(candidate.SourceEntityId.Value, out _) &&
                     _contract.TryGet(candidate.TargetEntityId.Value, out _) =>
                new(candidate.SourceEntityId.Value, candidate.TargetEntityId.Value,
                    [new SemanticLexicalStep(token, candidate, candidate.Score, [])], candidate.Score),

            SemanticLexicalCandidateKind.Traversal
                when candidate.SourceEntityId is not null && candidate.TargetEntityId is not null &&
                     _contract.TryGet(candidate.SourceEntityId.Value, out _) &&
                     _contract.TryGet(candidate.TargetEntityId.Value, out _) =>
                new(candidate.SourceEntityId.Value, candidate.TargetEntityId.Value,
                    [new SemanticLexicalStep(token, candidate, candidate.Score, [])], candidate.Score),

            SemanticLexicalCandidateKind.Field or SemanticLexicalCandidateKind.Value
                when candidate.EntityId is not null && _contract.TryGet(candidate.EntityId.Value, out _) =>
                new(candidate.EntityId.Value, candidate.EntityId.Value,
                    [new SemanticLexicalStep(token, candidate, candidate.Score, [])], candidate.Score),

            _ => null
        };
    }

    private IReadOnlyList<Transition> ResolveTransition(SearchState state, SemanticLexicalCandidate candidate,
        SearchBudget budget)
    {
        var owner = candidate.Kind switch
        {
            SemanticLexicalCandidateKind.Entity or SemanticLexicalCandidateKind.Node => candidate.EntityId,
            SemanticLexicalCandidateKind.Field or SemanticLexicalCandidateKind.Value => candidate.EntityId,
            SemanticLexicalCandidateKind.Relationship or SemanticLexicalCandidateKind.Traversal => candidate
                .SourceEntityId,
            _ => null
        };

        if (owner is null)
            return [];

        if (owner.Value == state.CurrentEntity)
            return [new(1.0d, [])];

        var path = FindPath(state.CurrentEntity, owner.Value, _maxBridgeHops, budget);
        if (path.Count == 0)
            return [];

        // A bridge is legal, but deliberately penalized. Direct neighbours beat
        // longer inferred paths when lexical scores are otherwise comparable.
        var factor = Math.Pow(0.90d, path.Count);
        return [new(factor, path)];
    }

    /// <summary>Bridging BFS between two entities. Bounded two ways: structurally
    /// by <paramref name="maxHops"/> (graph depth), and by <paramref name="budget"/>
    /// (total search work shared with the outer DFS) — a permissive candidate
    /// source cannot turn a shallow-looking search into unbounded work just
    /// because the underlying entity graph is densely connected.</summary>
    private IReadOnlyList<SemanticLexicalCandidate> FindPath(EntityId source, EntityId target, int maxHops,
        SearchBudget budget)
    {
        if (source == target) return [];

        var queue = new Queue<(EntityId Entity, List<SemanticLexicalCandidate> Path)>();
        var visited = new HashSet<EntityId> { source };
        queue.Enqueue((source, []));

        while (queue.Count > 0)
        {
            if (budget.Tick()) return [];

            var (entityId, path) = queue.Dequeue();
            if (path.Count >= maxHops) continue;

            var entity = _contract.Get(entityId);
            foreach (var relationship in entity.Relationships)
            {
                var targetEntity = _contract.Get(relationship.Target);
                var hop = new SemanticLexicalCandidate(
                    relationship.Name,
                    SemanticLexicalCandidateKind.Relationship,
                    relationship.Name,
                    1,
                    RelationshipId: relationship.Id,
                    SourceEntityId: entity.Id,
                    TargetEntityId: targetEntity.Id);
                var nextPath = path.Concat([hop]).ToList();
                if (targetEntity.Id == target)
                    return nextPath;
                if (visited.Add(targetEntity.Id))
                    queue.Enqueue((targetEntity.Id, nextPath));
            }
        }

        return [];
    }

    private static string[] Tokenize(string expression) =>
        expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim(',', '.', ';', ':', '?', '!', '(', ')', '[', ']', '{', '}'))
            .Where(x => x.Length > 0)
            .ToArray();

    private sealed record Transition(double Factor, IReadOnlyList<SemanticLexicalCandidate> BridgingPath);

    private sealed record SearchState(
        EntityId RootEntity,
        EntityId CurrentEntity,
        IReadOnlyList<SemanticLexicalStep> Steps,
        double GraphFactor)
    {
        public SearchState Add(SemanticLexicalStep step, double factor) =>
            new(RootEntity, ResolveCurrent(step.Candidate), Steps.Append(step).ToArray(), GraphFactor * factor);

        private static EntityId ResolveCurrent(SemanticLexicalCandidate candidate) =>
            candidate.TargetEntityId
            ?? candidate.EntityId
            ?? candidate.SourceEntityId
            ?? throw new InvalidOperationException("Lexical candidate has no semantic entity context.");
    }

    /// <summary>
    /// Tracks total search work across one <see>
    ///     <cref>Ground</cref>
    /// </see>
    /// call so the
    /// combined DFS (over tokens/candidates) and bridging BFS (over graph
    /// hops) share a single resource ceiling. Once any limit is hit the
    /// budget latches <see cref="Exceeded"/> permanently for that call —
    /// callers must stop expanding and unwind rather than keep searching,
    /// since a search that stopped early cannot prove it enumerated every
    /// legal interpretation.
    /// </summary>
    private sealed class SearchBudget
    {
        private readonly int _maxNodes;
        private readonly TimeSpan _maxElapsed;
        private readonly CancellationToken _cancellationToken;
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _nodesVisited;

        public SearchBudget(int maxNodes, TimeSpan maxElapsed, CancellationToken cancellationToken)
        {
            _maxNodes = maxNodes;
            _maxElapsed = maxElapsed;
            _cancellationToken = cancellationToken;
        }

        public bool Exceeded { get; private set; }

        public GroundingBudgetLimit LimitHit { get; private set; } = GroundingBudgetLimit.None;

        /// <summary>Call once per unit of search work (one DFS node expansion,
        /// one BFS dequeue). Returns true the moment any limit has fired —
        /// including on every subsequent call for the rest of this
        /// <see>
        ///     <cref>Ground</cref>
        /// </see>
        /// invocation — so callers can bail out
        /// immediately instead of finishing the current loop.</summary>
        public bool Tick()
        {
            if (Exceeded)
                return true;

            if (_cancellationToken.IsCancellationRequested)
            {
                Exceeded = true;
                LimitHit = GroundingBudgetLimit.Cancelled;
                return true;
            }

            if (++_nodesVisited > _maxNodes)
            {
                Exceeded = true;
                LimitHit = GroundingBudgetLimit.MaxPathsExplored;
                return true;
            }

            if (_stopwatch.Elapsed > _maxElapsed)
            {
                Exceeded = true;
                LimitHit = GroundingBudgetLimit.Timeout;
                return true;
            }

            return false;
        }
    }
}

/// <summary>Thrown internally when candidate retrieval for a single token
/// exceeds the configured retrieval timeout. Caught at the <see cref="SemanticLexicalResolver.Ground(string, CancellationToken)"/>
/// boundary and translated into a fail-closed <see cref="GroundingOutcome.BudgetExceeded"/>
/// result rather than propagated to the caller as an exception.</summary>
public sealed class GroundingRetrievalTimeoutException(string token, TimeSpan elapsed) : Exception(
    $"Candidate retrieval for token '{token}' exceeded the retrieval timeout after {elapsed.TotalMilliseconds:0}ms.")
{
    public string Token { get; } = token;
    public TimeSpan Elapsed { get; } = elapsed;
}