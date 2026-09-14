namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Deterministically composes rewrite rules while enforcing ordering, conflicts,
/// selection, idempotence and termination limits. Proof obligations remain per application.
/// </summary>
public sealed class RewriteRuleComposer
{
    private readonly IReadOnlyList<IPlanRewriteRule> _orderedRules;
    private readonly RewriteRuleCompositionOptions _options;
    private readonly RewriteRuleSelector _selector;
    private readonly IProviderCostEstimator? _providerCostEstimator;

    public RewriteRuleComposer(
        IEnumerable<IPlanRewriteRule> rules,
        RewriteRuleCompositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _options = (options ?? new()).Validate();
        _orderedRules = OrderRules(rules.ToArray());
        _providerCostEstimator = _options.ProviderCostEstimator;
        _selector = new RewriteRuleSelector(
            _options.SelectionPolicy,
            _providerCostEstimator,
            _options.ProviderCostSelectionPolicy);
    }

    public RewriteRuleCompositionResult Compose(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var current = plan;
        var applications = new List<PlanRewriteRuleResult>();
        var selectionHistory = new List<RewriteRuleCandidate>();
        var providerSelectionHistory = new List<ProviderAwareRewriteRuleCandidate>();
        var seenPlans = new HashSet<string>(StringComparer.Ordinal)
        {
            SemanticPlanFingerprint.Create(current)
        };
        var appliedIdempotent = new HashSet<string>(StringComparer.Ordinal);
        var appliedRules = new HashSet<string>(StringComparer.Ordinal);

        for (var pass = 0;; pass++)
        {
            var eligible = _orderedRules
                .Where(rule => !(rule.IsIdempotent && appliedIdempotent.Contains(rule.Name)))
                .Where(rule => rule.MustRunAfter.All(appliedRules.Contains))
                // A rule that must run before another rule is not itself
                // blocked. Instead, block the dependent rule while the
                // prerequisite remains applicable. This is important when
                // selection uses benefit/cost rather than raw priority.
                .Where(rule => _orderedRules
                    .Where(prerequisite => prerequisite.MustRunBefore.Contains(rule.Name, StringComparer.Ordinal))
                    .All(prerequisite =>
                        appliedRules.Contains(prerequisite.Name) ||
                        !prerequisite.CanApply(current)))
                .Where(rule => !rule.ConflictsWith.Any(appliedRules.Contains))
                .ToArray();

            string? selectedRuleName;
            if (_providerCostEstimator is not null)
            {
                var selectedProvider = _selector.SelectProviderAware(current, eligible);
                if (selectedProvider is null)
                {
                    return new RewriteRuleCompositionResult(
                        current,
                        applications,
                        applications.Sum(x => x.CostImpact),
                        true,
                        selectionHistory,
                        providerSelectionHistory);
                }

                providerSelectionHistory.Add(selectedProvider);
                selectedRuleName = selectedProvider.RuleName;
            }
            else
            {
                var selected = _selector.Select(current, eligible);
                if (selected is null)
                {
                    return new RewriteRuleCompositionResult(
                        current,
                        applications,
                        applications.Sum(x => x.CostImpact),
                        true,
                        selectionHistory,
                        providerSelectionHistory);
                }

                selectionHistory.Add(selected);
                selectedRuleName = selected.RuleName;
            }

            var rule = _orderedRules.First(r => StringComparer.Ordinal.Equals(r.Name, selectedRuleName));

            if (applications.Count >= _options.MaxRuleApplications)
                throw new InvalidOperationException(
                    "Rewrite rule composition exceeded the maximum rule-application budget.");

            var candidate = rule.Apply(current);
            if (ReferenceEquals(candidate, current))
            {
                appliedRules.Add(rule.Name);
                if (rule.IsIdempotent)
                    appliedIdempotent.Add(rule.Name);
                continue;
            }

            var result = SemanticPlanOptimizer.ApplyRule(rule, current, candidate);
            if (!result.IsSatisfied)
                throw new InvalidOperationException(
                    $"Rewrite rule '{rule.Name}' did not satisfy its proof obligations.");

            var currentFingerprint = SemanticPlanFingerprint.Create(current);
            var fingerprint = SemanticPlanFingerprint.Create(candidate);

            // A rule may rebuild an equivalent object graph while producing
            // exactly the same executable plan fingerprint. That is a
            // canonicalization no-op, not a rewrite cycle. Treat it as
            // unchanged and allow the composer to mark the rule complete.
            if (StringComparer.Ordinal.Equals(currentFingerprint, fingerprint))
            {
                appliedRules.Add(rule.Name);
                if (rule.IsIdempotent)
                    appliedIdempotent.Add(rule.Name);
                continue;
            }

            if (!seenPlans.Add(fingerprint))
                throw new InvalidOperationException(
                    $"Rewrite rule composition detected a cycle at rule '{rule.Name}'.");
            if (seenPlans.Count > _options.MaxPlanVisits)
                throw new InvalidOperationException("Rewrite rule composition exceeded the maximum plan-visit budget.");

            applications.Add(result);
            current = candidate;
            appliedRules.Add(rule.Name);
            if (rule.IsIdempotent)
                appliedIdempotent.Add(rule.Name);

            if (pass >= _options.MaxRuleApplications)
                throw new InvalidOperationException(
                    "Rewrite rule composition exceeded the maximum composition passes.");
        }
    }

    private static IReadOnlyList<IPlanRewriteRule> OrderRules(IReadOnlyList<IPlanRewriteRule> rules)
    {
        var byName = rules.ToDictionary(r => r.Name, StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var dependency in rule.MustRunAfter.Concat(rule.MustRunBefore))
            {
                if (!byName.ContainsKey(dependency))
                    throw new InvalidOperationException(
                        $"Rewrite rule '{rule.Name}' references unknown rule '{dependency}'.");
            }

            foreach (var conflict in rule.ConflictsWith)
            {
                if (byName.ContainsKey(conflict) &&
                    byName[conflict].ConflictsWith.Contains(rule.Name, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"Rewrite rules '{rule.Name}' and '{conflict}' conflict and cannot be composed.");
            }
        }

        var edges = rules.ToDictionary(r => r.Name, _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var indegree = rules.ToDictionary(r => r.Name, _ => 0, StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var after in rule.MustRunAfter)
                AddEdge(after, rule.Name);
            foreach (var before in rule.MustRunBefore)
                AddEdge(rule.Name, before);
        }

        var queue = new PriorityQueue<IPlanRewriteRule, (int Priority, string Name)>();
        foreach (var rule in rules.Where(r => indegree[r.Name] == 0))
            queue.Enqueue(rule, (rule.Priority, rule.Name));

        var ordered = new List<IPlanRewriteRule>(rules.Count);
        while (queue.TryDequeue(out var rule, out _))
        {
            ordered.Add(rule);
            foreach (var next in edges[rule.Name])
            {
                if (--indegree[next] == 0)
                {
                    var candidate = byName[next];
                    queue.Enqueue(candidate, (candidate.Priority, candidate.Name));
                }
            }
        }

        if (ordered.Count != rules.Count)
            throw new InvalidOperationException("Rewrite rule composition contains an ordering cycle.");

        return ordered;

        void AddEdge(string from, string to)
        {
            if (edges[from].Add(to))
                indegree[to]++;
        }
    }
}