package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import graphql.language.ArrayValue;
import graphql.language.IntValue;
import graphql.language.ListType;
import graphql.language.NonNullType;
import graphql.language.ObjectField;
import graphql.language.ObjectValue;
import graphql.language.StringValue;
import graphql.language.Type;
import graphql.language.TypeName;
import graphql.language.Value;
import graphql.language.VariableDefinition;
import java.math.BigInteger;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Port of the C# GraphQLInputCoercionTests / variable-coercion behaviour at the coercer boundary. */
class GraphQLVariableCoercionTest {
    private static Map<String, VariableDefinition> defs(String name, Type<?> type, Value<?> def) {
        var b = VariableDefinition.newVariableDefinition().name(name).type(type);
        if (def != null) b.defaultValue(def);
        return Map.of(name, b.build());
    }

    private static Type<?> nonNull(String n) { return new NonNullType(new TypeName(n)); }

    private static Map<String, Object> vars(String k, Object v) {
        var m = new HashMap<String, Object>();
        m.put(k, v);
        return m;
    }

    @Test
    void non_null_variable_is_required() {
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("input", null, defs("input", nonNull("CustomerInput"), null)));
        assertTrue(ex.getMessage().contains("$input"));
        assertTrue(ex.getMessage().contains("non-null"));
    }

    @Test
    void nullable_variable_missing_value_resolves_to_null() {
        assertNull(GraphQLVariableCoercer.resolve("input", null, defs("input", new TypeName("CustomerInput"), null)));
    }

    @Test
    void undeclared_variable_is_rejected() {
        var ex = assertThrows(IllegalStateException.class, () -> GraphQLVariableCoercer.resolve("x", null, Map.of()));
        assertTrue(ex.getMessage().contains("is not defined"));
    }

    @Test
    @SuppressWarnings("unchecked")
    void default_input_object_is_coerced_case_insensitively() {
        var def = ObjectValue.newObjectValue()
                .objectField(ObjectField.newObjectField().name("name").value(StringValue.of("Ada")).build()).build();
        var value = (Map<String, Object>) GraphQLVariableCoercer.resolve("input", null,
                defs("input", new TypeName("CustomerInput"), def));
        assertEquals("Ada", value.get("NAME"));
    }

    @Test
    void scalar_variable_is_coerced_to_declared_type() {
        assertEquals("Ada", GraphQLVariableCoercer.resolve("name", vars("name", "Ada"), defs("name", nonNull("String"), null)));
        assertEquals(7, GraphQLVariableCoercer.resolve("n", vars("n", 7L), defs("n", nonNull("Int"), null)));
        assertEquals(7.0, GraphQLVariableCoercer.resolve("f", vars("f", 7), defs("f", nonNull("Float"), null)));
        assertEquals("42", GraphQLVariableCoercer.resolve("id", vars("id", 42), defs("id", nonNull("ID"), null)));
        assertEquals(true, GraphQLVariableCoercer.resolve("b", vars("b", true), defs("b", nonNull("Boolean"), null)));
    }

    @Test
    void wrong_scalar_type_is_rejected() {
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("name", vars("name", 123), defs("name", nonNull("String"), null)));
        assertTrue(ex.getMessage().contains("String"));
        assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("n", vars("n", 1.5d), defs("n", nonNull("Int"), null)));
        assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("n", vars("n", 3_000_000_000L), defs("n", nonNull("Int"), null)));
        assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("b", vars("b", "true"), defs("b", nonNull("Boolean"), null)));
    }

    @Test
    void null_for_non_null_variable_is_rejected() {
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("name", vars("name", null), defs("name", nonNull("String"), null)));
        assertTrue(ex.getMessage().contains("cannot be null"));
    }

    @Test
    void explicit_null_for_nullable_variable_is_null_even_with_a_default() {
        assertNull(GraphQLVariableCoercer.resolve("s", vars("s", null),
                defs("s", new TypeName("String"), StringValue.of("dflt"))));
        assertEquals("dflt", GraphQLVariableCoercer.resolve("s", null, defs("s", new TypeName("String"), StringValue.of("dflt"))));
    }

    @Test
    void list_variable_accepts_singleton_value() {
        var type = new NonNullType(new ListType(nonNull("String")));
        assertEquals(List.of("Name"), GraphQLVariableCoercer.resolve("names", vars("names", "Name"), defs("names", type, null)));
        assertEquals(List.of("A", "B"),
                GraphQLVariableCoercer.resolve("names", vars("names", List.of("A", "B")), defs("names", type, null)));
        assertEquals(List.of("A"),
                GraphQLVariableCoercer.resolve("names", vars("names", new String[] {"A"}), defs("names", type, null)));
    }

    @Test
    void list_elements_are_coerced_with_indexed_paths() {
        var type = new ListType(nonNull("Int"));
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("xs", vars("xs", List.of(1, "x")), defs("xs", type, null)));
        assertTrue(ex.getMessage().contains("$xs[1]"), ex.getMessage());
    }

    @Test
    void object_supplied_for_a_list_is_rejected() {
        var type = new ListType(nonNull("String"));
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLVariableCoercer.resolve("xs", vars("xs", Map.of("a", 1)), defs("xs", type, null)));
        assertTrue(ex.getMessage().contains("must be a list"));
    }

    @Test
    void extra_supplied_variable_is_ignored() {
        var supplied = new LinkedHashMap<String, Object>();
        supplied.put("name", "Ada");
        supplied.put("unused", true);
        GraphQLVariableCoercer.validateSuppliedVariables(supplied, defs("name", nonNull("String"), null));
        assertEquals("Ada", GraphQLVariableCoercer.resolve("name", supplied, defs("name", nonNull("String"), null)));
    }

    @Test
    void duplicate_case_insensitive_object_keys_are_rejected() {
        var supplied = new LinkedHashMap<String, Object>();
        supplied.put("a", 1);
        supplied.put("A", 2);
        assertThrows(IllegalArgumentException.class,
                () -> GraphQLVariableCoercer.resolve("input", vars("input", supplied), defs("input", new TypeName("In"), null)));
    }

    @Test
    void resolve_value_maps_literals_and_resolves_variables() {
        var d = defs("name", nonNull("String"), null);
        var vs = vars("name", "Ada");
        assertEquals("x", GraphQLVariableCoercer.resolveValue(StringValue.of("x"), vs, d));
        assertEquals(5L, GraphQLVariableCoercer.resolveValue(IntValue.of(5), vs, d));
        assertEquals("123456789012345678901234567890",
                GraphQLVariableCoercer.resolveValue(new IntValue(new BigInteger("123456789012345678901234567890")), vs, d));
        assertEquals(List.of("x", 5L), GraphQLVariableCoercer.resolveValue(
                ArrayValue.newArrayValue().values(List.of(StringValue.of("x"), IntValue.of(5))).build(), vs, d));
        assertEquals("Ada", GraphQLVariableCoercer.resolveValue(graphql.language.VariableReference.of("name"), vs, d));
        assertEquals("RED", GraphQLVariableCoercer.resolveValue(graphql.language.EnumValue.of("RED"), vs, d));
        assertNull(GraphQLVariableCoercer.resolveValue(graphql.language.NullValue.of(), vs, d));
    }
}
