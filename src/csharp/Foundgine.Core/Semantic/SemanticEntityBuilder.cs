using System.Linq.Expressions;
using System.Reflection;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic;

/// <summary>
/// Small hand-authored construction path. AOT generation can target these
/// same semantic shapes later.
/// </summary>
/// <summary>
/// Model-independent semantic entity builder. Use this when an intent/semantic
/// contract is not backed by a CLR model type.
/// </summary>
public sealed class SemanticEntityBuilder
{
    private readonly EntityId _id;
    private readonly string _name;
    private readonly List<SemanticField> _fields = [];
    private readonly List<SemanticRelationship> _relationships = [];
    private SemanticFieldIdentity? _identity;
    private readonly List<SemanticAlias> _aliases = [];

    internal SemanticEntityBuilder(EntityId id, string name)
    {
        _id = id;
        _name = name;
    }

    public SemanticEntityBuilder Identity(FieldId fieldId, string name)
    {
        _identity = new SemanticFieldIdentity(fieldId, name);
        return this;
    }

    public SemanticEntityBuilder Alias(string alias, int? weight = null)
    {
        AddAlias(_aliases, alias, weight);
        return this;
    }

    /// <summary>Declares multiple aliases in one call, e.g. <c>.Aliases("Item", "Item2")</c>
    /// or <c>.Aliases(["Item", "Item2"])</c>. Equivalent to calling <see cref="Alias"/> once per name.</summary>
    public SemanticEntityBuilder Aliases(params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        foreach (var alias in aliases)
            AddAlias(_aliases, alias, null);
        return this;
    }

    public SemanticEntityBuilder Field(
        FieldId id,
        string name,
        Type clrType,
        SemanticType? semanticType = null,
        SemanticFieldCapabilities capabilities = SemanticFieldCapabilities.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clrType);
        _fields.Add(new SemanticField(id, name, clrType, semanticType, capabilities));
        return this;
    }

    public SemanticEntityBuilder Constraint(FieldId fieldId, SemanticConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        var index = _fields.FindIndex(x => x.Id == fieldId);
        if (index < 0) throw new InvalidOperationException($"Field '{fieldId}' is not declared on '{_name}'.");
        var field = _fields[index];
        _fields[index] = field with { Constraints = field.EffectiveConstraints.Concat([constraint]).ToArray() };
        return this;
    }

    public SemanticEntityBuilder FieldAlias(FieldId fieldId, string alias, int? weight = null)
    {
        var index = _fields.FindIndex(x => x.Id == fieldId);
        if (index < 0) throw new InvalidOperationException($"Field '{fieldId}' is not declared on '{_name}'.");
        var field = _fields[index];
        var aliases = field.EffectiveAliases.Concat([new SemanticAlias(alias, weight)]).ToArray();
        _fields[index] = field with { Aliases = aliases };
        return this;
    }

    /// <summary>Declares multiple field aliases in one call.</summary>
    public SemanticEntityBuilder FieldAliases(FieldId fieldId, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var index = _fields.FindIndex(x => x.Id == fieldId);
        if (index < 0) throw new InvalidOperationException($"Field '{fieldId}' is not declared on '{_name}'.");
        var field = _fields[index];
        var merged = field.EffectiveAliases.Concat(aliases.Select(a => new SemanticAlias(a, null))).ToArray();
        _fields[index] = field with { Aliases = merged };
        return this;
    }

    public SemanticEntityBuilder Relationship(
        RelationshipId id,
        string name,
        EntityId target,
        RelationshipCardinality cardinality)
    {
        _relationships.Add(new SemanticRelationship(id, name, target, cardinality));
        return this;
    }

    public SemanticEntityBuilder RelationshipAlias(RelationshipId relationshipId, string alias, int? weight = null)
    {
        var index = _relationships.FindIndex(x => x.Id == relationshipId);
        if (index < 0)
            throw new InvalidOperationException($"Relationship '{relationshipId}' is not declared on '{_name}'.");
        var relationship = _relationships[index];
        var aliases = relationship.EffectiveAliases.Concat([new SemanticAlias(alias, weight)]).ToArray();
        _relationships[index] = relationship with { Aliases = aliases };
        return this;
    }

    internal SemanticEntity Build() =>
        new(
            _id,
            _name,
            _identity ?? throw new InvalidOperationException(
                $"Semantic entity '{_name}' must declare an identity."),
            _fields.ToArray(),
            _relationships.ToArray(),
            _aliases.ToArray());

    private static void AddAlias(List<SemanticAlias> aliases, string alias, int? weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        if (aliases.Any(x => string.Equals(x.Name, alias, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Duplicate semantic alias '{alias}'.", nameof(alias));
        aliases.Add(new SemanticAlias(alias, weight));
    }
}

/// <summary>
/// Optional strongly typed semantic entity builder. Property selectors target
/// the application/domain model type <typeparamref name="TModel"/>. This is a
/// convenience and validation path; semantic intent does not require a CLR model.
/// </summary>
/// <typeparam name="TModel">The application/domain model represented by the semantic entity.</typeparam>
public sealed class SemanticEntityBuilder<TModel>
{
    private readonly EntityId _id;
    private readonly string _name;
    private readonly List<SemanticField> _fields = [];
    private readonly List<SemanticRelationship> _relationships = [];
    private SemanticFieldIdentity? _identity;
    private readonly List<SemanticAlias> _aliases = [];

    internal SemanticEntityBuilder(EntityId id, string name)
    {
        _id = id;
        _name = name;
    }

    public SemanticEntityBuilder<TModel> Alias(string alias, int? weight = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (_aliases.Any(x =>
                string.Equals(x.Name, alias, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Duplicate semantic alias '{alias}'.",
                nameof(alias));
        }

        _aliases.Add(new SemanticAlias(alias, weight));
        return this;
    }

    /// <summary>Declares multiple aliases in one call, e.g. <c>.Aliases("Item", "Item2")</c>
    /// or <c>.Aliases(["Item", "Item2"])</c>. Equivalent to calling <see cref="Alias"/> once per name.</summary>
    public SemanticEntityBuilder<TModel> Aliases(params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        foreach (var alias in aliases)
            Alias(alias);
        return this;
    }

    /// <summary>
    /// Declares the semantic identity using a property on <typeparamref name="TModel"/>.
    /// Foundgine derives the identity field id deterministically from the
    /// semantic entity and field name; callers do not need to construct a
    /// <see cref="FieldId"/>.
    /// </summary>
    public SemanticEntityBuilder<TModel> Identity<TProperty>(Expression<Func<TModel, TProperty>> property,
        string? semanticName = null)
    {
        var metadata = GetProperty(property);
        var fieldName = semanticName ?? metadata.Name;
        _identity = new SemanticFieldIdentity(
            FieldId.Create(_name, fieldName),
            fieldName);
        return this;
    }

    /// <summary>
    /// Exposes a property from <typeparamref name="TModel"/> as a semantic field.
    /// The CLR type, field name, and entity-local field identity are derived from
    /// the property selector.
    /// </summary>
    public SemanticEntityBuilder<TModel> Field<TProperty>(
        Expression<Func<TModel, TProperty>> property,
        SemanticType? semanticType = null,
        SemanticFieldCapabilities capabilities = SemanticFieldCapabilities.Default)
    {
        var metadata = GetProperty(property);
        _fields.Add(new SemanticField(
            FieldId.Create(_name, metadata.Name),
            metadata.Name,
            metadata.PropertyType,
            semanticType,
            capabilities,
            NullableOverride: GetNullability(metadata)));
        return this;
    }

    public SemanticEntityBuilder<TModel> Constraint<TProperty>(Expression<Func<TModel, TProperty>> property,
        SemanticConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        var fieldId = FieldId.Create(_name, GetProperty(property).Name);
        var index = _fields.FindIndex(x => x.Id == fieldId);
        if (index < 0) throw new InvalidOperationException($"Field '{fieldId}' is not declared on '{_name}'.");
        var field = _fields[index];
        _fields[index] = field with { Constraints = field.EffectiveConstraints.Concat([constraint]).ToArray() };
        return this;
    }

    public SemanticEntityBuilder<TModel> FieldAlias<TProperty>(Expression<Func<TModel, TProperty>> property,
        string alias, int? weight = null)
    {
        var fieldId = FieldId.Create(_name, GetProperty(property).Name);
        var index = _fields.FindIndex(x => x.Id == fieldId);
        if (index < 0) throw new InvalidOperationException($"Field '{fieldId}' is not declared on '{_name}'.");
        var field = _fields[index];
        _fields[index] = field with
        {
            Aliases = field.EffectiveAliases.Concat([new SemanticAlias(alias, weight)]).ToArray()
        };
        return this;
    }

    /// <summary>Declares multiple field aliases in one call.</summary>
    public SemanticEntityBuilder<TModel> FieldAliases<TProperty>(Expression<Func<TModel, TProperty>> property,
        params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var fieldId = FieldId.Create(_name, GetProperty(property).Name);
        var index = _fields.FindIndex(x => x.Id == fieldId);
        if (index < 0) throw new InvalidOperationException($"Field '{fieldId}' is not declared on '{_name}'.");
        var field = _fields[index];
        var merged = field.EffectiveAliases.Concat(aliases.Select(a => new SemanticAlias(a, null))).ToArray();
        _fields[index] = field with { Aliases = merged };
        return this;
    }

    /// <summary>
    /// Declares a relationship with strongly typed property selectors on both
    /// sides. <typeparamref name="TModel"/> is the source/domain model and
    /// <typeparamref name="TTargetModel"/> is the target/domain model. The
    /// selected properties are validated during semantic-model construction;
    /// they are not semantic-entity or provider metadata properties.
    /// </summary>
    /// <summary>
    /// Declares a relationship and derives its stable identity from the source
    /// semantic entity name and relationship name. Manual numeric relationship
    /// identities are intentionally not required for new code.
    /// </summary>
    public SemanticEntityBuilder<TModel> Relationship<TTargetModel>(
        string name,
        Expression<Func<TModel, object?>> fromProperty,
        Expression<Func<TTargetModel, object?>> toProperty,
        EntityId target,
        RelationshipCardinality cardinality) =>
        Relationship(
            RelationshipId.Create(_name, name),
            name,
            fromProperty,
            toProperty,
            target,
            cardinality);

    public SemanticEntityBuilder<TModel> Relationship<TTargetModel>(
        RelationshipId id,
        string name,
        Expression<Func<TModel, object?>> fromProperty,
        Expression<Func<TTargetModel, object?>> toProperty,
        EntityId target,
        RelationshipCardinality cardinality)
    {
        var from = GetProperty(fromProperty);
        var to = GetProperty<TTargetModel>(toProperty);

        if (from.PropertyType != to.PropertyType)
        {
            throw new ArgumentException(
                $"Relationship '{_name}.{name}' maps '{typeof(TModel).Name}.{from.Name}' ({from.PropertyType.Name}) to '{typeof(TTargetModel).Name}.{to.Name}' ({to.PropertyType.Name}); both properties must have the same CLR type.");
        }

        _relationships.Add(new SemanticRelationship(id, name, target, cardinality));
        return this;
    }

    public SemanticEntityBuilder<TModel> RelationshipAlias(RelationshipId relationshipId, string alias,
        int? weight = null)
    {
        var index = _relationships.FindIndex(x => x.Id == relationshipId);
        if (index < 0)
            throw new InvalidOperationException($"Relationship '{relationshipId}' is not declared on '{_name}'.");
        var relationship = _relationships[index];
        _relationships[index] = relationship with
        {
            Aliases = relationship.EffectiveAliases.Concat([new SemanticAlias(alias, weight)]).ToArray()
        };
        return this;
    }

    /// <summary>Declares multiple relationship aliases in one call.</summary>
    public SemanticEntityBuilder<TModel> RelationshipAliases(RelationshipId relationshipId, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var index = _relationships.FindIndex(x => x.Id == relationshipId);
        if (index < 0)
            throw new InvalidOperationException($"Relationship '{relationshipId}' is not declared on '{_name}'.");
        var relationship = _relationships[index];
        var merged = relationship.EffectiveAliases.Concat(aliases.Select(a => new SemanticAlias(a, null))).ToArray();
        _relationships[index] = relationship with { Aliases = merged };
        return this;
    }

    public SemanticEntityBuilder<TModel> Relationship(
        RelationshipId id,
        string name,
        EntityId target,
        RelationshipCardinality cardinality)
    {
        _relationships.Add(new SemanticRelationship(id, name, target, cardinality));
        return this;
    }

    internal SemanticEntity Build() =>
        new(
                _id,
                _name,
                _identity ?? throw new InvalidOperationException(
                    $"Semantic entity '{_name}' must declare an identity."),
                _fields.ToArray(),
                _relationships.ToArray(),
                _aliases.ToArray())
            { ModelType = typeof(TModel) };

    private static PropertyInfo GetProperty<TTargetModel>(Expression<Func<TTargetModel, object?>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            body = conversion.Operand;

        if (body is not MemberExpression { Member: PropertyInfo property })
        {
            throw new ArgumentException(
                $"The semantic relationship property selector must be a direct property access on {typeof(TTargetModel).Name}, such as x => x.Id.",
                nameof(expression));
        }

        if (property.DeclaringType is null || !property.DeclaringType.IsAssignableFrom(typeof(TTargetModel)))
        {
            throw new ArgumentException(
                $"Property '{property.Name}' does not belong to model type '{typeof(TTargetModel).FullName}'.",
                nameof(expression));
        }

        return property;
    }

    private static bool? GetNullability(PropertyInfo property)
    {
        // Runtime Type cannot distinguish string from string?. NullabilityInfo
        // preserves the compiler-produced nullable-reference contract when it
        // is available. Unknown generic/legacy metadata deliberately falls back
        // to SemanticField's CLR-type inference.
        if (property.PropertyType.IsValueType)
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;

        var state = new NullabilityInfoContext().Create(property).ReadState;
        return state switch
        {
            NullabilityState.Nullable => true,
            NullabilityState.NotNull => false,
            _ => null
        };
    }

    private static PropertyInfo GetProperty<TProperty>(Expression<Func<TModel, TProperty>> expression) =>
        GetProperty(expression, nameof(expression));

    private static PropertyInfo GetProperty<TProperty>(Expression<Func<TModel, TProperty>> expression,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(expression);

        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            body = conversion.Operand;

        if (body is not MemberExpression { Member: PropertyInfo property })
        {
            throw new ArgumentException(
                $"The semantic property selector must be a direct property access on {typeof(TModel).Name}, such as x => x.Id.",
                nameof(expression));
        }

        if (property.DeclaringType is null || !property.DeclaringType.IsAssignableFrom(typeof(TModel)))
        {
            throw new ArgumentException(
                $"Property '{property.Name}' does not belong to model type '{typeof(TModel).FullName}'.",
                nameof(expression));
        }

        return property;
    }
}