using System.Globalization;
using System.Linq.Expressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>
/// Connects one semantic model to storage entities (<see cref="ConnectionMetadata"/>):
/// which members correspond, how values are converted, and who may follow the connection.
/// This is the fluent counterpart of <c>[FoundgineConnection]</c>, <c>[FoundgineConversion]</c>
/// and <c>[FoundgineAuthorization]</c>. Relationship foreign keys stay in
/// <see cref="FoundgineConnectionConfiguration"/>.
/// </summary>
public abstract class FoundgineModelConnectionConfiguration<TModel> : IFoundgineConfiguration
{
    public abstract void Configure(FoundgineModelConnectionBuilder<TModel> connections);

    void IFoundgineConfiguration.Apply(FoundgineConfigurationContext context) =>
        Configure(new FoundgineModelConnectionBuilder<TModel>(context));
}

public sealed class FoundgineModelConnectionBuilder<TModel>
{
    private readonly FoundgineConfigurationContext _context;

    internal FoundgineModelConnectionBuilder(FoundgineConfigurationContext context) => _context = context;

    /// <summary>
    /// Declares a connection from the model member <paramref name="member"/> (for example
    /// <c>x =&gt; x.Customer</c>) to the storage entity <typeparamref name="TEntity"/>.
    /// The connection name defaults to the member name.
    /// </summary>
    public FoundgineModelConnectionBuilder<TModel, TEntity> HasConnection<TEntity>(
        Expression<Func<TModel, object?>> member)
    {
        var name = ConfigurationExpression.Member(member).Name;
        return new FoundgineModelConnectionBuilder<TModel, TEntity>(
            _context.AddModelConnection(typeof(TModel), typeof(TEntity), name));
    }
}

public sealed class FoundgineModelConnectionBuilder<TModel, TEntity>
{
    private readonly ModelConnectionDefinition _definition;

    internal FoundgineModelConnectionBuilder(ModelConnectionDefinition definition) => _definition = definition;

    /// <summary>Overrides the connection name (defaults to the member name).</summary>
    public FoundgineModelConnectionBuilder<TModel, TEntity> HasName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _definition.Name = name.Trim();
        return this;
    }

    /// <summary>Pins the connection id instead of deriving it from the model and connection names.</summary>
    public FoundgineModelConnectionBuilder<TModel, TEntity> HasId(ulong id)
    {
        if (id == 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Connection id must be non-zero.");
        _definition.ExplicitId = id;
        return this;
    }

    /// <summary>Maps a model member to an entity member of the same type.</summary>
    public FoundgineModelConnectionBuilder<TModel, TEntity> Maps<TValue>(
        Expression<Func<TModel, TValue>> source,
        Expression<Func<TEntity, TValue>> target)
    {
        _definition.Fields.Add(new ModelConnectionField(
            ConfigurationExpression.Member(source),
            ConfigurationExpression.Member(target),
            null));
        return this;
    }

    /// <summary>
    /// Maps a model member to an entity member of a different type, converting the value with
    /// <paramref name="convert"/>. The delegate is recorded as <see cref="ConversionMetadata"/>
    /// for the (<typeparamref name="TSource"/>, <typeparamref name="TTarget"/>) pair; Foundgine
    /// never calls it itself.
    /// </summary>
    public FoundgineModelConnectionBuilder<TModel, TEntity> Maps<TSource, TTarget>(
        Expression<Func<TModel, TSource>> source,
        Expression<Func<TEntity, TTarget>> target,
        Func<TSource, TTarget> convert)
    {
        ArgumentNullException.ThrowIfNull(convert);

        _definition.Fields.Add(new ModelConnectionField(
            ConfigurationExpression.Member(source),
            ConfigurationExpression.Member(target),
            ConversionMetadata.FromDelegate(convert)));
        return this;
    }

    /// <summary>
    /// Attaches an authorization predicate to the connection, for example
    /// <c>(user, customer) =&gt; user.TenantId == customer.TenantId</c>. Only member access,
    /// constants, ==, !=, &amp;&amp;, || and ! are supported, the same subset as the attribute path.
    /// The first parameter is the caller context; the second is the connected entity.
    /// </summary>
    public FoundgineModelConnectionBuilder<TModel, TEntity> Authorize<TContext>(
        string name,
        Expression<Func<TContext, TEntity, bool>> predicate,
        ulong? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(predicate);

        var translated = AuthorizationPredicateTranslator.TryTranslate(predicate, out var error);
        _definition.Authorizations.Add(new ModelConnectionAuthorization(
            name.Trim(),
            id,
            typeof(TContext),
            typeof(TEntity),
            predicate.ToString(),
            translated,
            error));
        return this;
    }
}

/// <summary>
/// Lowers an authorization expression tree to <see cref="AuthorizationPredicate"/>, producing the
/// same shape the AOT generator derives from the attribute path's lambda source.
/// </summary>
internal static class AuthorizationPredicateTranslator
{
    public static AuthorizationPredicate? TryTranslate(LambdaExpression lambda, out string? error)
    {
        error = null;
        var result = Node(lambda.Body, lambda.Parameters, ref error);
        return error is null ? result : null;
    }

    private static AuthorizationPredicate? Node(
        Expression expression,
        IReadOnlyList<ParameterExpression> parameters,
        ref string? error)
    {
        switch (expression)
        {
            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                return Node(convert.Operand, parameters, ref error);

            case UnaryExpression { NodeType: ExpressionType.Not } not:
            {
                var operand = Node(not.Operand, parameters, ref error);
                return operand is null ? null : AuthorizationPredicate.Not(operand);
            }

            case ParameterExpression parameter:
                if (parameter == parameters[0])
                    return AuthorizationPredicate.ContextParameter(parameter.Name ?? "context");
                if (parameters.Count > 1 && parameter == parameters[1])
                    return AuthorizationPredicate.ResourceParameter(parameter.Name ?? "resource");
                error ??= $"Unknown parameter '{parameter.Name}'.";
                return null;

            case MemberExpression { Expression: not null } member:
            {
                var target = Node(member.Expression, parameters, ref error);
                return target is null ? null : AuthorizationPredicate.Member(target, member.Member.Name);
            }

            case ConstantExpression constant:
                return AuthorizationPredicate.Constant(FormatConstant(constant.Value));

            case BinaryExpression binary:
            {
                Func<AuthorizationPredicate, AuthorizationPredicate, AuthorizationPredicate>? create = binary.NodeType switch
                {
                    ExpressionType.Equal => AuthorizationPredicate.Equal,
                    ExpressionType.NotEqual => AuthorizationPredicate.NotEqual,
                    ExpressionType.AndAlso => AuthorizationPredicate.And,
                    ExpressionType.OrElse => AuthorizationPredicate.Or,
                    _ => null
                };

                if (create is null)
                {
                    error ??= $"Operator '{binary.NodeType}' is not supported in authorization predicates.";
                    return null;
                }

                var left = Node(binary.Left, parameters, ref error);
                var right = Node(binary.Right, parameters, ref error);
                return left is null || right is null ? null : create(left, right);
            }

            default:
                error ??= $"Expression '{expression}' ({expression.NodeType}) is not supported in authorization predicates. " +
                          "Use member access, constants, ==, !=, &&, || and !; captured variables are not supported.";
                return null;
        }
    }

    // Matches the literal text the generator records (string literals keep their quotes).
    private static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };
}
