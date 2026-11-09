package com.foundgine.extensions.graphql;

import graphql.language.Argument;
import graphql.language.BooleanValue;
import graphql.language.Directive;
import graphql.language.NullValue;
import graphql.language.Value;
import graphql.language.VariableDefinition;
import graphql.language.VariableReference;
import java.util.List;
import java.util.Map;

/**
 * Evaluates the standard GraphQL {@code @include} and {@code @skip} directives at the adapter boundary. No directive
 * concepts are exposed to Foundgine core. Port of {@code GraphQLDirectiveEvaluator}.
 */
public final class GraphQLDirectiveEvaluator {
    private GraphQLDirectiveEvaluator() { }

    public static boolean shouldInclude(
            List<Directive> directives, Map<String, Object> variables, Map<String, VariableDefinition> variableDefinitions) {
        for (var directive : directives) {
            var name = directive.getName();
            if (!name.equals("include") && !name.equals("skip"))
                throw new IllegalStateException("GraphQL directive '@" + name
                        + "' is not supported by the GraphQL adapter. Supported directives are @include and @skip.");

            Argument ifArgument = null;
            for (var a : directive.getArguments()) {
                if (a.getName().equals("if")) {
                    ifArgument = a;
                    break;
                }
            }
            if (ifArgument == null)
                throw new IllegalStateException("GraphQL directive '@" + name + "' requires an 'if' argument.");

            boolean condition = resolveBoolean(ifArgument.getValue(), variables, variableDefinitions);
            if (name.equals("skip") && condition) return false;
            if (name.equals("include") && !condition) return false;
        }
        return true;
    }

    private static boolean resolveBoolean(
            Value<?> node, Map<String, Object> variables, Map<String, VariableDefinition> variableDefinitions) {
        Object value;
        if (node instanceof BooleanValue b) value = b.isValue();
        else if (node instanceof VariableReference v)
            value = GraphQLVariableCoercer.resolve(v.getName(), variables, variableDefinitions);
        else if (node instanceof NullValue) value = null;
        else throw new IllegalStateException("GraphQL @include/@skip 'if' must be a Boolean or Boolean variable.");

        if (value instanceof Boolean b) return b;
        throw new IllegalStateException("GraphQL @include/@skip 'if' must resolve to a Boolean.");
    }
}
