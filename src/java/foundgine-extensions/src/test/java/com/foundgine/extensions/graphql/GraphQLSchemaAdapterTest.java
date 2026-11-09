package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModelBuilder;
import org.junit.jupiter.api.Test;

/** Port of the C# GraphQLIntrospectionTests (schema descriptor / SDL). */
class GraphQLSchemaAdapterTest {
    private static com.foundgine.core.semantic.SemanticModel customerAccountModel() {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        return new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class)
                        .relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.MANY))
                .entity(account, "Account", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Balance", java.math.BigDecimal.class))
                .build();
    }

    @Test
    void schema_descriptor_exposes_query_entities_relationships_and_mutations() {
        var schema = new GraphQLSchemaAdapter(customerAccountModel()).build();
        var query = schema.queryTypes().get(0).fields();
        var mutation = schema.mutationTypes().get(0).fields();

        assertTrue(query.stream().anyMatch(x -> x.name().equals("customer") && x.type().equals("Customer")));
        for (var op : new String[] {"createCustomer", "updateCustomer", "deleteCustomer", "upsertCustomer"})
            assertTrue(mutation.stream().anyMatch(x -> x.name().equals(op)), op);

        var input = schema.inputTypes().stream().filter(x -> x.name().equals("CustomerInput")).findFirst().orElseThrow();
        assertTrue(input.fields().stream().anyMatch(x -> x.name().equals("name") && x.type().equals("String")));
        var where = schema.inputTypes().stream().filter(x -> x.name().equals("CustomerWhereInput")).findFirst().orElseThrow();
        assertTrue(where.fields().stream().anyMatch(x -> x.name().equals("id") && x.type().equals("Int")));
    }

    @Test
    void relationship_and_scalar_mapping_follow_the_csharp_vocabulary() {
        var sdl = new GraphQLSchemaAdapter(customerAccountModel()).buildSdl();

        assertTrue(sdl.contains("accounts: [Account!]!"), sdl);
        assertTrue(sdl.contains("balance: Float"), sdl);
        assertTrue(sdl.contains("upsertCustomer(input: CustomerInput!, onConflict: [String!]): Customer!"), sdl);
        assertTrue(sdl.contains("updateCustomer(input: CustomerInput!, where: CustomerWhereInput!): Customer!"), sdl);
    }

    @Test
    void build_sdl_contains_introspection_compatible_schema_vocabulary() {
        var model = new SemanticModelBuilder()
                .entity(new EntityId(1), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class))
                .build();

        var sdl = new GraphQLSchemaAdapter(model).buildSdl();

        for (var expected : new String[] {"type Query", "customer: Customer!", "type Customer", "id: ID!",
                "name: String", "type Mutation", "createCustomer(input: CustomerInput!): Customer!",
                "input CustomerInput", "input CustomerWhereInput"})
            assertTrue(sdl.contains(expected), expected);
    }

    @Test
    void schema_generation_is_deterministic_and_ordered_by_entity_name() {
        var model = new SemanticModelBuilder()
                .entity(new EntityId(2), "Zebra", e -> e.identity(new FieldId(1), "Id"))
                .entity(new EntityId(1), "Alpha", e -> e.identity(new FieldId(1), "Id"))
                .build();
        var adapter = new GraphQLSchemaAdapter(model);

        assertEquals(adapter.buildSdl(), adapter.buildSdl());
        assertTrue(adapter.buildSdl().indexOf("alpha") < adapter.buildSdl().indexOf("zebra"));
    }

    @Test
    void type_mapper_matches_the_csharp_mapping() {
        assertEquals("String", GraphQLTypeMapper.map(String.class));
        assertEquals("String", GraphQLTypeMapper.map(java.util.UUID.class));
        assertEquals("Boolean", GraphQLTypeMapper.map(boolean.class));
        assertEquals("Int", GraphQLTypeMapper.map(long.class));
        assertEquals("Int", GraphQLTypeMapper.map(Short.class));
        assertEquals("Float", GraphQLTypeMapper.map(double.class));
        assertEquals("Float", GraphQLTypeMapper.map(java.math.BigDecimal.class));
        assertEquals("DateTime", GraphQLTypeMapper.map(java.time.Instant.class));
        assertEquals("DateTime", GraphQLTypeMapper.map(java.time.OffsetDateTime.class));
        assertEquals("String", GraphQLTypeMapper.map(java.time.LocalDate.class)); // C# DateOnly stays String
        assertEquals("TimeUnit", GraphQLTypeMapper.map(java.util.concurrent.TimeUnit.class));
        assertEquals("String", GraphQLTypeMapper.map(Object.class));
    }
}
