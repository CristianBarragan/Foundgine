using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.IR.Graph;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Security.Execution;

namespace Foundgine.Core.Semantic.Intent;

/// <summary>
/// Compiles human/agent-friendly structured intent into the canonical
/// SemanticRequest. It performs only semantic-name resolution; authorization,
/// planning, and execution remain later stages.
/// </summary>
public sealed class ReadIntentCompiler
{
    private readonly SemanticModel? _model;
    private readonly SemanticContractSnapshot? _contract;

    public ReadIntentCompiler(SemanticModel model) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    public ReadIntentCompiler(SemanticContractSnapshot contract) =>
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));

    /// <summary>
    /// Compiles a dynamic read intent against the frozen runtime contract and
    /// lowers it to the same canonical operation graph consumed by the planner.
    /// </summary>
    public SemanticOperationGraph CompileOperationGraph(ReadIntent intent, SecurityResourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var request = Compile(intent);
        var contract = _contract ??
                       (_model ?? throw new InvalidOperationException("No semantic contract is configured.")).Freeze()
                       .CreateSnapshot();
        var graph = new SemanticRequestResolver(contract).Resolve(request);
        var operationGraph = SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));
        SemanticOperationGraphSafetyValidator.Validate(operationGraph, limits ?? new SecurityResourceLimits());
        return operationGraph;
    }

    /// <summary>Creates a contract-bound dynamic intent document.</summary>
    public SemanticIntentDocument CreateDocument(ReadIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return new SemanticIntentDocument(ContractFingerprint, intent).Validate();
    }

    /// <summary>Resolves a contract-bound intent document and rejects stale contracts before resolution.</summary>
    public SemanticIntentResolution ResolveDocument(SemanticIntentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();

        if (!string.Equals(document.ContractFingerprint, ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Semantic intent document is bound to contract '{document.ContractFingerprint}', but the resolver uses '{ContractFingerprint}'.");

        var request = Compile(document.Intent);
        return new SemanticIntentResolution(document, request, ContractFingerprint);
    }

    /// <summary>Resolves a document directly to the canonical operation graph.</summary>
    public SemanticOperationGraph ResolveDocumentGraph(SemanticIntentDocument document)
    {
        var resolution = ResolveDocument(document);
        var contract = _contract ??
                       (_model ?? throw new InvalidOperationException("No semantic contract is configured.")).Freeze()
                       .CreateSnapshot();
        var graph = new SemanticRequestResolver(contract).Resolve(resolution.Request);
        return SemanticOperationGraph.Create(SemanticOperationCompiler.Compile(graph));
    }

    public string ContractFingerprint =>
        _contract?.ContractFingerprint ??
        (_model ?? throw new InvalidOperationException("No semantic contract is configured.")).Freeze()
        .ContractFingerprint;

    public SemanticRequest Compile(ReadIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var root = FindEntity(intent.RootEntity);
        var selections = intent.Selections.Select(selection =>
            CompileSelection(root, selection)).ToArray();

        var filter = intent.Filter is null ? null : CompileFilter(root, intent.Filter);
        var order = intent.Order?.Select(o => CompileOrder(root, o)).ToArray();

        return new SemanticRequest(
            root.Id,
            selections,
            new SemanticQueryOptions(filter, order, intent.Limit, intent.Offset, intent.After),
            intent.Security);
    }

    private SemanticSelection CompileSelection(SemanticEntity entity, ReadSelection selection)
    {
        if ((selection.Field is null) == (selection.Relationship is null))
            throw Invalid("A selection must specify exactly one field or relationship.");

        if (selection.Field is not null)
        {
            var field = FindField(entity, selection.Field);
            if (selection.EffectiveChildren.Count != 0)
                throw Invalid($"Field '{entity.Name}.{field.Name}' cannot have children.");
            return new SemanticSelection(field.Id, null, []);
        }

        if (selection.EffectiveChildren.Count == 0)
            throw Invalid($"Relationship '{entity.Name}.{selection.Relationship}' requires child selections.");

        var path = ResolveRelationshipPath(entity, selection.Relationship!);
        var compiledChildren = selection.EffectiveChildren.ToArray();

        // A logical traversal is deliberately expanded into its real semantic
        // relationship chain here. From this point onward the authorization
        // layer sees every hop, so tenant/field/relationship policies cannot be
        // bypassed by using a convenient alias such as Customer.transactions.
        IReadOnlyList<SemanticSelection> children = compiledChildren
            .Select(child => CompileSelection(GetEntity(path[^1].Target), child))
            .ToArray();

        for (var i = path.Count - 1; i >= 0; i--)
        {
            children = [new SemanticSelection(null, path[i].Id, children)];
        }

        return children[0];
    }

    private SemanticFilterExpression CompileFilter(SemanticEntity entity, ReadFilter filter) => filter switch
    {
        ReadFieldFilter field => new SemanticFieldFilter(
            FindField(entity, field.Field).Id,
            field.Operator,
            field.Value),

        ReadRelationshipFilter relationship => CompileRelationshipFilter(entity, relationship),

        ReadAndFilter andFilter when andFilter.Expressions.Count > 0 =>
            new SemanticAndFilter(andFilter.Expressions.Select(x => CompileFilter(entity, x)).ToArray()),

        ReadOrFilter orFilter when orFilter.Expressions.Count > 0 =>
            new SemanticOrFilter(orFilter.Expressions.Select(x => CompileFilter(entity, x)).ToArray()),

        ReadAndFilter => throw Invalid("AND filter cannot be empty."),
        ReadOrFilter => throw Invalid("OR filter cannot be empty."),
        _ => throw Invalid($"Unsupported read filter '{filter.GetType().Name}'.")
    };

    private SemanticOrderTerm CompileOrder(SemanticEntity root, ReadOrder order)
    {
        var entity = root;
        var path = new List<RelationshipId>();

        foreach (var relationshipName in order.EffectivePath)
        {
            var relationshipPath = ResolveRelationshipPath(entity, relationshipName);
            foreach (var relationship in relationshipPath)
            {
                path.Add(relationship.Id);
                entity = GetEntity(relationship.Target);
            }
        }

        var fieldId = order.Aggregate == SemanticOrderAggregate.Count
            ? entity.Identity.FieldId
            : FindField(entity, order.Field).Id;

        return new SemanticOrderTerm(
            fieldId,
            order.Direction,
            path,
            order.Aggregate);
    }

    private SemanticEntity FindEntity(string name)
    {
        if (_contract is not null)
            return _contract.TryResolveEntity(name, out var entity)
                ? entity
                : throw Invalid($"Unknown entity '{name}'.");

        return _model!.Entities.FirstOrDefault(x =>
                   string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
                   x.EffectiveAliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
               ?? throw Invalid($"Unknown entity '{name}'.");
    }

    private SemanticEntity GetEntity(EntityId id) =>
        _contract?.Get(id) ?? _model!.Get(id);

    private static SemanticField FindField(SemanticEntity entity, string name)
    {
        var field = entity.Fields.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
            x.EffectiveAliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (field is not null)
            return field;

        if (string.Equals(entity.Identity.Name, name, StringComparison.OrdinalIgnoreCase))
            return new SemanticField(entity.Identity.FieldId, entity.Identity.Name, typeof(object));

        throw Invalid($"Unknown field '{entity.Name}.{name}'.");
    }

    private IReadOnlyList<SemanticRelationship> ResolveRelationshipPath(SemanticEntity entity, string name)
    {
        var direct = entity.Relationships.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
            x.EffectiveAliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (direct is not null)
            return [direct];

        SemanticTraversal? traversal = null;

        if (_contract is not null)
        {
            _contract.TryGetTraversal(entity.Id, name, out traversal);
        }
        else
        {
            _model!.TryGetTraversal(entity.Id, name, out traversal);
        }

        if (traversal is not null)
        {
            var current = entity;
            var result = new List<SemanticRelationship>(traversal.Path.Count);
            foreach (var relationshipId in traversal.Path)
            {
                var relationship = current.Relationships.FirstOrDefault(x => x.Id == relationshipId)
                                   ?? throw Invalid(
                                       $"Traversal '{name}' contains relationship '{relationshipId}' that is not defined on '{current.Name}'.");
                result.Add(relationship);
                current = GetEntity(relationship.Target);
            }

            return result;
        }

        throw Invalid($"Unknown relationship or semantic traversal '{entity.Name}.{name}'.");
    }

    private SemanticFilterExpression CompileRelationshipFilter(
        SemanticEntity entity,
        ReadRelationshipFilter filter)
    {
        var path = ResolveRelationshipPath(entity, filter.Relationship);
        if (path.Count > 1 && filter.Quantifier is not SemanticRelationshipQuantifier.Some)
        {
            throw Invalid(
                $"Logical traversal filter '{entity.Name}.{filter.Relationship}' currently supports only 'Some' quantification across multi-hop paths. " +
                "None/All require an explicit path-quantifier algebra so their meaning cannot be changed by expansion.");
        }

        SemanticFilterExpression predicate = CompileFilter(
            GetEntity(path[^1].Target),
            filter.Predicate);

        for (var i = path.Count - 1; i >= 0; i--)
            predicate = new SemanticRelationshipFilter(
                path[i].Id,
                i == path.Count - 1 ? filter.Quantifier : SemanticRelationshipQuantifier.Some,
                predicate);

        return predicate;
    }

    private static InvalidOperationException Invalid(string message) =>
        new($"Invalid read intent: {message}");
}