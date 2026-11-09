package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import java.util.List;

/** Describes how a translated request's rows map back to GraphQL response keys. */
public record GraphQLResultShape(List<GraphQLResultField> fields) {
    public GraphQLResultShape {
        fields = List.copyOf(fields);
    }

    /**
     * One response field. {@code field} / {@code relationship} are null when the GraphQL field is not backed by one
     * (for example {@code id} resolved from identity); {@code children} is null for scalars.
     */
    public record GraphQLResultField(
            FieldId field, RelationshipId relationship, String graphQLName, String alias, GraphQLResultShape children) {
        public GraphQLResultField(FieldId field, RelationshipId relationship, String graphQLName, String alias) {
            this(field, relationship, graphQLName, alias, null);
        }
    }
}
