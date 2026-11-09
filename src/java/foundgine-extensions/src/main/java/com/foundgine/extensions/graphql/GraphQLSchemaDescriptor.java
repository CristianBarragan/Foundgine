package com.foundgine.extensions.graphql;

import java.util.List;

/**
 * Provider-neutral description of the GraphQL schema exposed by the adapter. It contains GraphQL-facing names and
 * Java-derived scalar types, but no graphql-java runtime objects. Hosts can use it (or the SDL from
 * {@link GraphQLSchemaAdapter#buildSdl()}) to construct a real graphql-java schema, whose standard introspection then
 * exposes {@code __schema} and {@code __type}.
 */
public record GraphQLSchemaDescriptor(
        List<GraphQLObjectTypeDescriptor> queryTypes,
        List<GraphQLObjectTypeDescriptor> mutationTypes,
        List<GraphQLInputTypeDescriptor> inputTypes) {
    public GraphQLSchemaDescriptor {
        queryTypes = List.copyOf(queryTypes);
        mutationTypes = List.copyOf(mutationTypes);
        inputTypes = List.copyOf(inputTypes);
    }

    public record GraphQLObjectTypeDescriptor(String name, List<GraphQLFieldDescriptor> fields) {
        public GraphQLObjectTypeDescriptor {
            fields = List.copyOf(fields);
        }
    }

    public record GraphQLFieldDescriptor(
            String name, String type, boolean isList, boolean isNonNull, List<GraphQLArgumentDescriptor> arguments) {
        public GraphQLFieldDescriptor {
            arguments = arguments == null ? List.of() : List.copyOf(arguments);
        }

        public GraphQLFieldDescriptor(String name, String type) {
            this(name, type, false, false, null);
        }

        public GraphQLFieldDescriptor(String name, String type, boolean isList, boolean isNonNull) {
            this(name, type, isList, isNonNull, null);
        }
    }

    public record GraphQLArgumentDescriptor(String name, String type, boolean isList, boolean isNonNull) {
        public GraphQLArgumentDescriptor(String name, String type) {
            this(name, type, false, false);
        }
    }

    public record GraphQLInputTypeDescriptor(String name, List<GraphQLInputFieldDescriptor> fields) {
        public GraphQLInputTypeDescriptor {
            fields = List.copyOf(fields);
        }
    }

    public record GraphQLInputFieldDescriptor(String name, String type, boolean isList, boolean isNonNull) {
        public GraphQLInputFieldDescriptor(String name, String type) {
            this(name, type, false, false);
        }
    }
}
