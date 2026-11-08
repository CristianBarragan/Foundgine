package com.foundgine.extensions.graphql;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import java.util.List;

/**
 * Shared fixtures for the mutation-adapter tests. The C# test classes each repeat a private {@code BuildCustomer()}
 * returning {@code (SemanticModel, MetadataRegistry)}; the Java ports share this one definition (same ids, names and
 * CLR-to-Java type mapping: {@code typeof(long)} -> {@code long.class} (primitive = non-nullable, which the value coercer relies on),
 * {@code long?} -> {@code Long.class}, {@code string} -> {@link String}).
 */
final class MutationAdapterFixtures {
    private MutationAdapterFixtures() {}

    /** C# {@code (SemanticModel Model, MetadataRegistry Registry)} tuple. */
    record Built(SemanticModel model, MetadataRegistry registry) {
        GraphQLMutationAdapter adapter() {
            return new GraphQLMutationAdapter(model, registry);
        }
    }

    static final EntityId CUSTOMER = new EntityId(1);
    static final EntityId ACCOUNT = new EntityId(2);

    /** Customer(Id, Name) with Id as primary key. */
    static Built customer() {
        var registry = new MetadataRegistry();
        registry.register(new EntityMetadata(
                CUSTOMER,
                "Customer",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")),
                null,
                List.of(
                        new FieldMetadata(new FieldId(1), "Id", long.class, new ColumnReference(CUSTOMER, new ColumnId(1))),
                        new FieldMetadata(new FieldId(2), "Name", String.class, new ColumnReference(CUSTOMER, new ColumnId(2)))),
                new ColumnReference(CUSTOMER, new ColumnId(1)),
                null,
                false,
                null,
                null));

        var model = new SemanticModelBuilder()
                .entity(CUSTOMER, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class))
                .build();

        return new Built(model, registry);
    }

    /** Customer(Id, Name) 1..n Account(Id, CustomerId, Name) through relationship {@code Accounts}. */
    static Built customerWithAccounts() {
        var customerId = new ColumnId(1);
        var customerName = new ColumnId(2);
        var accountId = new ColumnId(1);
        var accountCustomerId = new ColumnId(2);
        var accountName = new ColumnId(3);
        var relationshipId = new RelationshipId(1);

        var registry = new MetadataRegistry();
        registry.register(new EntityMetadata(
                CUSTOMER,
                "Customer",
                List.of(new ColumnMetadata(customerId, "Id"), new ColumnMetadata(customerName, "Name")),
                null,
                List.of(
                        new FieldMetadata(new FieldId(1), "Id", long.class, new ColumnReference(CUSTOMER, customerId)),
                        new FieldMetadata(new FieldId(2), "Name", String.class, new ColumnReference(CUSTOMER, customerName))),
                new ColumnReference(CUSTOMER, customerId),
                null,
                false,
                null,
                null));
        registry.register(new EntityMetadata(
                ACCOUNT,
                "Account",
                List.of(
                        new ColumnMetadata(accountId, "Id"),
                        new ColumnMetadata(accountCustomerId, "CustomerId"),
                        new ColumnMetadata(accountName, "Name")),
                null,
                List.of(
                        new FieldMetadata(new FieldId(1), "Id", long.class, new ColumnReference(ACCOUNT, accountId)),
                        new FieldMetadata(new FieldId(2), "CustomerId", long.class, new ColumnReference(ACCOUNT, accountCustomerId)),
                        new FieldMetadata(new FieldId(3), "Name", String.class, new ColumnReference(ACCOUNT, accountName))),
                new ColumnReference(ACCOUNT, accountId),
                null,
                false,
                null,
                null));
        registry.register(new RelationshipMetadata(
                relationshipId,
                CUSTOMER,
                ACCOUNT,
                "Accounts",
                new ColumnReference(CUSTOMER, customerId),
                new ColumnReference(ACCOUNT, accountCustomerId)));

        var model = new SemanticModelBuilder()
                .entity(CUSTOMER, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class)
                        .relationship(relationshipId, "Accounts", ACCOUNT, RelationshipCardinality.MANY))
                .entity(ACCOUNT, "Account", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "CustomerId", long.class)
                        .field(new FieldId(3), "Name", String.class))
                .build();

        return new Built(model, registry);
    }

    /** Customer(Id, Name) 1..n Account(Id, Name); relationship keys are customer.Id -> account.Id (MutationVariableTests). */
    static Built customerWithSimpleAccounts() {
        var relationship = new RelationshipId(1);
        var registry = new MetadataRegistry();
        registry.register(new EntityMetadata(
                CUSTOMER, "Customer",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")),
                null,
                List.of(
                        new FieldMetadata(new FieldId(1), "Id", long.class, new ColumnReference(CUSTOMER, new ColumnId(1))),
                        new FieldMetadata(new FieldId(2), "Name", String.class, new ColumnReference(CUSTOMER, new ColumnId(2)))),
                new ColumnReference(CUSTOMER, new ColumnId(1)), null, false, null, null));
        registry.register(new EntityMetadata(
                ACCOUNT, "Account",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")),
                null,
                List.of(
                        new FieldMetadata(new FieldId(1), "Id", long.class, new ColumnReference(ACCOUNT, new ColumnId(1))),
                        new FieldMetadata(new FieldId(2), "Name", String.class, new ColumnReference(ACCOUNT, new ColumnId(2)))),
                new ColumnReference(ACCOUNT, new ColumnId(1)), null, false, null, null));
        registry.register(new RelationshipMetadata(
                relationship, CUSTOMER, ACCOUNT, "Accounts",
                new ColumnReference(CUSTOMER, new ColumnId(1)),
                new ColumnReference(ACCOUNT, new ColumnId(1))));

        var model = new SemanticModelBuilder()
                .entity(CUSTOMER, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class)
                        .relationship(relationship, "Accounts", ACCOUNT, RelationshipCardinality.MANY))
                .entity(ACCOUNT, "Account", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class))
                .build();
        return new Built(model, registry);
    }

    /** Customer(Id, CustomerKey: Guid). The Java builder cannot mark a reference type non-nullable (see 9b notes). */
    static Built guidCustomer() {
        var registry = new MetadataRegistry();
        registry.register(new EntityMetadata(
                CUSTOMER, "Customer",
                List.of(new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "CustomerKey")),
                null,
                List.of(
                        new FieldMetadata(new FieldId(1), "Id", long.class, new ColumnReference(CUSTOMER, new ColumnId(1))),
                        new FieldMetadata(new FieldId(2), "CustomerKey", java.util.UUID.class, new ColumnReference(CUSTOMER, new ColumnId(2)))),
                new ColumnReference(CUSTOMER, new ColumnId(1)), null, false, null, null));
        var model = new SemanticModelBuilder()
                .entity(CUSTOMER, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "CustomerKey", java.util.UUID.class))
                .build();
        return new Built(model, registry);
    }

    /** C# {@code Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase)}. */
    static boolean containsIgnoreCase(String message, String expected) {
        return message != null && message.toLowerCase(java.util.Locale.ROOT).contains(expected.toLowerCase(java.util.Locale.ROOT));
    }
}
