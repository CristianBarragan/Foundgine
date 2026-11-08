package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import graphql.language.Argument;
import graphql.language.BooleanValue;
import graphql.language.Directive;
import graphql.language.NonNullType;
import graphql.language.NullValue;
import graphql.language.StringValue;
import graphql.language.TypeName;
import graphql.language.Value;
import graphql.language.VariableDefinition;
import graphql.language.VariableReference;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Port of the directive semantics of the C# GraphQLDirectiveTests at the evaluator boundary. */
class GraphQLDirectiveEvaluatorTest {
    private static Directive d(String name, Value<?> ifValue) {
        var b = Directive.newDirective().name(name);
        if (ifValue != null) b.arguments(List.of(new Argument("if", ifValue)));
        return b.build();
    }

    private static final Map<String, VariableDefinition> DEFS = Map.of("flag",
            VariableDefinition.newVariableDefinition().name("flag").type(new NonNullType(new TypeName("Boolean"))).build(),
            "text", VariableDefinition.newVariableDefinition().name("text").type(new TypeName("String")).build());

    private static boolean include(List<Directive> ds, Map<String, Object> vars) {
        return GraphQLDirectiveEvaluator.shouldInclude(ds, vars, DEFS);
    }

    @Test
    void no_directives_includes() {
        assertTrue(include(List.of(), null));
    }

    @Test
    void include_and_skip_literals() {
        assertTrue(include(List.of(d("include", BooleanValue.of(true))), null));
        assertFalse(include(List.of(d("include", BooleanValue.of(false))), null));
        assertFalse(include(List.of(d("skip", BooleanValue.of(true))), null));
        assertTrue(include(List.of(d("skip", BooleanValue.of(false))), null));
    }

    @Test
    void variables_drive_the_condition() {
        assertTrue(include(List.of(d("include", VariableReference.of("flag"))), Map.of("flag", true)));
        assertFalse(include(List.of(d("include", VariableReference.of("flag"))), Map.of("flag", false)));
        assertFalse(include(List.of(d("skip", VariableReference.of("flag"))), Map.of("flag", true)));
    }

    @Test
    void all_directives_must_agree() {
        assertFalse(include(List.of(d("include", BooleanValue.of(true)), d("skip", BooleanValue.of(true))), null));
        assertTrue(include(List.of(d("include", BooleanValue.of(true)), d("skip", BooleanValue.of(false))), null));
    }

    @Test
    void unsupported_directive_is_rejected() {
        var ex = assertThrows(IllegalStateException.class, () -> include(List.of(d("defer", BooleanValue.of(true))), null));
        assertTrue(ex.getMessage().contains("@defer"));
        assertTrue(ex.getMessage().contains("@include and @skip"));
    }

    @Test
    void missing_if_argument_is_rejected() {
        var ex = assertThrows(IllegalStateException.class, () -> include(List.of(d("include", null)), null));
        assertTrue(ex.getMessage().contains("requires an 'if' argument"));
    }

    @Test
    void non_boolean_conditions_are_rejected() {
        assertThrows(IllegalStateException.class, () -> include(List.of(d("include", StringValue.of("yes"))), null));
        assertThrows(IllegalStateException.class, () -> include(List.of(d("include", NullValue.of())), null));
        assertThrows(IllegalStateException.class,
                () -> include(List.of(d("include", VariableReference.of("text"))), Map.of("text", "x")));
        assertThrows(IllegalStateException.class, () -> include(List.of(d("include", VariableReference.of("flag"))), null));
    }
}
