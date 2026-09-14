using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Resolution;

/// <summary>
/// Converts a committed lexical interpretation into a provider-neutral read intent.
/// Lexical grounding proposes meaning; the normal Foundgine execution pipeline remains
/// authoritative for authorization, planning, and provider execution.
/// </summary>
public sealed class SemanticLexicalReadIntentGrounder
{
    private readonly SemanticContractSnapshot _contract;
    private readonly SemanticLexicalResolver _resolver;

    public SemanticLexicalReadIntentGrounder(
        SemanticContractSnapshot contract,
        SemanticLexicalResolver resolver)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public ReadIntent Ground(
        string expression,
        CancellationToken cancellationToken = default)
    {
        var normalizedExpression = NormalizeIntentExpression(expression);
        var decision = _resolver.Ground(normalizedExpression, cancellationToken);
        if (decision.Outcome != GroundingOutcome.Committed || decision.Committed is null)
        {
            throw new InvalidOperationException(
                $"Lexical intent could not be committed: {decision.Outcome}. {decision.Reason}");
        }

        return BuildIntent(decision.Committed);
    }

    private static string NormalizeIntentExpression(string expression)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "show", "display", "list", "find", "get", "give", "return", "fetch",
            "please", "me", "my", "the", "a", "an", "all", "some", "what",
            "which", "who", "where", "that", "those", "these", "are", "is",
            "currently", "current", "from", "with", "for", "of", "to", "in", "on",
            "and", "by"
        };

        return string.Join(
            ' ',
            expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !ignored.Contains(token.Trim(',', '.', ';', ':', '?', '!'))));
    }

    private ReadIntent BuildIntent(GroundingInterpretation interpretation)
    {
        var root = _contract.Get(interpretation.RootEntity);
        var selections = new List<ReadSelection>();
        var filters = new List<ReadFilter>();

        foreach (var step in interpretation.Steps)
        {
            var candidate = step.Candidate;
            if (candidate.Kind is SemanticLexicalCandidateKind.Relationship or SemanticLexicalCandidateKind.Traversal)
            {
                if (candidate.RelationshipId is null || candidate.SourceEntityId is null)
                    continue;

                var route = FindEntityPath(root.Id, candidate.SourceEntityId.Value);
                route.Add(candidate.RelationshipId.Value);
                AddSelectionPath(selections, root.Id, route, null);
                continue;
            }

            if (candidate.Kind == SemanticLexicalCandidateKind.Field && candidate.FieldId is not null && candidate.EntityId is not null)
            {
                var route = FindEntityPath(root.Id, candidate.EntityId.Value);
                AddSelectionPath(selections, root.Id, route, candidate.FieldId.Value);
                continue;
            }

            // Value candidates produced by a lexical candidate source carry the field they
            // belong to. Treat that as an equality predicate. This is deliberately fail-closed:
            // a bare value without a semantic field cannot be guessed into a predicate.
            if (candidate.Kind == SemanticLexicalCandidateKind.Value)
            {
                if (candidate.FieldId is null || candidate.EntityId is null)
                    throw new InvalidOperationException(
                        $"Lexical value '{candidate.CanonicalName}' has no semantic field binding; intent was not committed to execution.");

                var route = FindEntityPath(root.Id, candidate.EntityId.Value);
                filters.Add(BuildValueFilter(route, candidate.EntityId.Value, candidate.FieldId.Value, candidate.Value));
                AddSelectionPath(selections, root.Id, route, candidate.FieldId.Value);
            }
        }

        if (selections.Count == 0)
            selections.Add(new ReadSelection(root.Identity.Name));

        return new ReadIntent(
            root.Name,
            selections,
            filters.Count switch
            {
                0 => null,
                1 => filters[0],
                _ => new ReadAndFilter(filters)
            });
    }

    private ReadFilter BuildValueFilter(
        IReadOnlyList<RelationshipId> route,
        EntityId owner,
        FieldId field,
        string? value)
    {
        var target = _contract.Get(owner);
        var fieldName = target.Fields.FirstOrDefault(x => x.Id == field)?.Name
                        ?? (target.Identity.FieldId == field ? target.Identity.Name : null)
                        ?? throw new InvalidOperationException($"Field {field} is not defined on '{target.Name}'.");

        ReadFilter result = new ReadFieldFilter(fieldName, SemanticFilterOperator.Eq, value);
        for (var i = route.Count - 1; i >= 0; i--)
        {
            var relationship = FindRelationshipById(route[i]);
            result = new ReadRelationshipFilter(
                relationship.Name,
                SemanticRelationshipQuantifier.Some,
                result);
        }

        return result;
    }

    private void AddSelectionPath(
        List<ReadSelection> selections,
        EntityId rootId,
        IReadOnlyList<RelationshipId> route,
        FieldId? field)
    {
        if (route.Count == 0)
        {
            if (field is null) return;
            var root = _contract.Get(rootId);
            var name = FindField(root, field.Value).Name;
            if (!selections.Any(x => string.Equals(x.Field, name, StringComparison.OrdinalIgnoreCase)))
                selections.Add(new ReadSelection(name));
            return;
        }

        var current = selections;
        for (var i = 0; i < route.Count; i++)
        {
            var relationship = FindRelationshipById(route[i]);
            var existing = current.FirstOrDefault(x => string.Equals(x.Relationship, relationship.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new ReadSelection(null, relationship.Name, []);
                current.Add(existing);
            }

            // ReadSelection is immutable. Replace the node so sibling paths merge rather
            // than overwrite each other.
            var index = current.IndexOf(existing);
            var children = existing.EffectiveChildren.ToList();
            if (i == route.Count - 1)
            {
                var target = _contract.Get(relationship.Target);
                var fieldName = field is not null
                    ? FindField(target, field.Value).Name
                    : target.Identity.Name;
                if (!children.Any(x => string.Equals(x.Field, fieldName, StringComparison.OrdinalIgnoreCase)))
                    children.Add(new ReadSelection(fieldName));
            }

            var replacement = existing with { Children = children };
            current[index] = replacement;
            current = children;
        }
    }

    private SemanticRelationship FindRelationshipById(RelationshipId id) =>
        _contract.Entities.SelectMany(x => x.Relationships)
            .FirstOrDefault(x => x.Id == id)
        ?? throw new InvalidOperationException($"Semantic relationship {id} is not defined.");

    private List<RelationshipId> FindEntityPath(EntityId source, EntityId target)
    {
        if (source == target)
            return [];

        var queue = new Queue<(EntityId Entity, List<RelationshipId> Path)>();
        var visited = new HashSet<EntityId> { source };
        queue.Enqueue((source, []));

        while (queue.Count > 0)
        {
            var (entityId, path) = queue.Dequeue();
            foreach (var relationship in _contract.Get(entityId).Relationships)
            {
                var next = path.Concat([relationship.Id]).ToList();
                if (relationship.Target == target)
                    return next;

                if (visited.Add(relationship.Target))
                    queue.Enqueue((relationship.Target, next));
            }
        }

        throw new InvalidOperationException($"No semantic path connects entity {source} to entity {target}.");
    }

    private SemanticField FindField(SemanticEntity entity, FieldId id) =>
        entity.Fields.FirstOrDefault(x => x.Id == id)
        ?? (entity.Identity.FieldId == id
            ? new SemanticField(entity.Identity.FieldId, entity.Identity.Name, typeof(object))
            : throw new InvalidOperationException($"Field {id} is not defined on '{entity.Name}'."));
}
