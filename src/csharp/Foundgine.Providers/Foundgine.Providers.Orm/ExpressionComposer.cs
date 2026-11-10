using System.Linq.Expressions;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Combines generated predicate fragments over a single entity parameter. Only expression-tree
/// rewriting is used; nothing is compiled or reflected over.
/// </summary>
public static class ExpressionComposer
{
    public static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    public static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    public static LambdaExpression And(LambdaExpression left, LambdaExpression right)
    {
        if (left.Parameters.Count != 1 || right.Parameters.Count != 1 || left.Parameters[0].Type != right.Parameters[0].Type ||
            left.ReturnType != typeof(bool) || right.ReturnType != typeof(bool))
            throw new InvalidOperationException("Predicate expressions must have one parameter of the same type and return bool.");
        var parameter = left.Parameters[0];
        var body = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda(Expression.AndAlso(left.Body, body), parameter);
    }

    public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> operand) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(operand.Body), operand.Parameters);

    public static Expression<Func<T, bool>> Constant<T>(bool value) => _ => value;

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> op)
    {
        var parameter = left.Parameters[0];
        var rebased = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(op(left.Body, rebased), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
