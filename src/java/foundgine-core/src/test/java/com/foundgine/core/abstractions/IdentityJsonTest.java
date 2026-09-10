package com.foundgine.core.abstractions;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;

/** Port of {@code SemanticIdentityJsonTests} (Foundgine.Semantics.Tests). */
class IdentityJsonTest {

    private final ObjectMapper mapper = new ObjectMapper();

    @ParameterizedTest
    @ValueSource(longs = {1L, 4294967297L})
    void allIdentityTypesSerializeAsNumericValuesAndRoundTrip(long value) throws Exception {
        Object[] values = {
                new EntityId(value),
                new FieldId(value),
                new RelationshipId(value),
                new ColumnId(value),
                new ModelId(value),
                new ConnectionId(value),
                new AuthorizationId(value)
        };

        for (Object identity : values) {
            String json = mapper.writeValueAsString(identity);
            assertEquals(UnsignedLongJson.toUnsignedString(value), json);
        }
    }

    @Test
    void fieldIdReadsLegacyObjectWireFormat() throws Exception {
        FieldId result = mapper.readValue("{\"Value\":4294967297}", FieldId.class);

        assertEquals(new FieldId(4294967297L), result);
    }

    @Test
    void entityIdReadsLegacyObjectWireFormat() throws Exception {
        EntityId result = mapper.readValue("{\"Value\":4294967297}", EntityId.class);

        assertEquals(new EntityId(4294967297L), result);
    }

    @Test
    void dictionaryKeyedByRelationshipIdSupports64BitValues() throws Exception {
        long value = 4294967297L;
        Map<RelationshipId, String> source = Map.of(new RelationshipId(value), "ok");

        String json = mapper.writeValueAsString(source);
        Map<RelationshipId, String> result = mapper.readValue(
                json, mapper.getTypeFactory().constructMapType(Map.class, RelationshipId.class, String.class));

        assertEquals("ok", result.get(new RelationshipId(value)));
    }
}
