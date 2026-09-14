using HotChocolate.Language;

namespace Foundgine.Extensions.GraphQL.HotChocolate;

/// <summary>
/// Evaluates the standard GraphQL @include and @skip directives at the
/// adapter boundary. No directive concepts are exposed to Foundgine core.
/// </summary>
public static class GraphQLDirectiveEvaluator
{
    public static bool ShouldInclude(
        IReadOnlyList<DirectiveNode> directives,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions)
    {
        foreach (var directive in directives)
        {
            var name = directive.Name.Value;
            if (!string.Equals(name, "include", StringComparison.Ordinal) &&
                !string.Equals(name, "skip", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"GraphQL directive '@{name}' is not supported by the GraphQL adapter. Supported directives are @include and @skip.");
            }

            var ifArgument =
                directive.Arguments.FirstOrDefault(x => string.Equals(x.Name.Value, "if", StringComparison.Ordinal));
            if (ifArgument is null)
                throw new InvalidOperationException($"GraphQL directive '@{name}' requires an 'if' argument.");

            var condition = ResolveBoolean(ifArgument.Value, variables, variableDefinitions);
            if (string.Equals(name, "skip", StringComparison.Ordinal) && condition)
                return false;
            if (string.Equals(name, "include", StringComparison.Ordinal) && !condition)
                return false;
        }

        return true;
    }

    private static bool ResolveBoolean(
        IValueNode node,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions)
    {
        object? value = node switch
        {
            BooleanValueNode boolean => boolean.Value,
            VariableNode variable => ResolveVariable(variable.Name.Value, variables, variableDefinitions),
            NullValueNode => null,
            _ => throw new InvalidOperationException(
                "GraphQL @include/@skip 'if' must be a Boolean or Boolean variable.")
        };

        if (value is bool booleanValue)
            return booleanValue;

        throw new InvalidOperationException("GraphQL @include/@skip 'if' must resolve to a Boolean.");
    }

    private static object? ResolveVariable(
        string name,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, VariableDefinitionNode> variableDefinitions) =>
        GraphQLVariableCoercer.Resolve(name, variables, variableDefinitions);
}