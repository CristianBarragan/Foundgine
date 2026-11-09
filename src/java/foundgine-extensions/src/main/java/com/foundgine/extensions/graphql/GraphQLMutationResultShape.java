package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import java.util.List;
import java.util.Objects;

/**
 * Port of C# {@code GraphQLMutationResultShape} and its field / relationship records: how a materialized mutation tree
 * maps back to GraphQL response keys.
 */
public record GraphQLMutationResultShape(
        List<GraphQLMutationResultField> fields, List<GraphQLMutationResultRelationship> relationships) {
    public GraphQLMutationResultShape {
        Objects.requireNonNull(fields, "fields");
        Objects.requireNonNull(relationships, "relationships");
        fields = List.copyOf(fields);
        relationships = List.copyOf(relationships);
    }

    public record GraphQLMutationResultField(FieldId field, String graphQLName, String alias) {
        /** C# secondary constructor: the response name doubles as the GraphQL name. */
        public GraphQLMutationResultField(FieldId field, String responseName) {
            this(field, responseName, responseName);
        }

        public String responseName() {
            return alias;
        }
    }

    public record GraphQLMutationResultRelationship(
            RelationshipId relationship,
            String graphQLName,
            String alias,
            GraphQLMutationResultShape shape,
            boolean isCollection) {
        public String responseName() {
            return alias;
        }
    }
}
