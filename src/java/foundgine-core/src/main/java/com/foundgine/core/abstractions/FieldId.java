package com.foundgine.core.abstractions;

import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.databind.BeanProperty;
import com.fasterxml.jackson.databind.DeserializationContext;
import com.fasterxml.jackson.databind.JavaType;
import com.fasterxml.jackson.databind.KeyDeserializer;
import com.fasterxml.jackson.databind.SerializerProvider;
import com.fasterxml.jackson.databind.annotation.JsonDeserialize;
import com.fasterxml.jackson.databind.annotation.JsonSerialize;
import com.fasterxml.jackson.databind.deser.std.StdDeserializer;
import com.fasterxml.jackson.databind.ser.std.StdSerializer;

import java.io.IOException;

/**
 * Port of {@code Foundgine.Core.Abstractions.FieldId} (a C# {@code readonly record struct}).
 *
 * <p>Stable identity of a semantic field.
 *
 * <p>{@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C# {@code ulong}.
 * The nested (de)serializers cover both the value position (equivalent to
 * {@code Read}/{@code Write} in the C# converter) and use as a {@code Map}
 * key (equivalent to {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = FieldId.Serializer.class, keyUsing = FieldId.KeySerializer.class)
@JsonDeserialize(using = FieldId.Deserializer.class, keyUsing = FieldId.KeyDeserializer.class)
public record FieldId(long value) implements SemanticId {

    public static FieldId create(String semanticEntityName, String semanticFieldName) {
        return new FieldId(SemanticIdentity.hash(SemanticIdentity.fieldKey(semanticEntityName, semanticFieldName)));
    }

    public static final class Serializer extends StdSerializer<FieldId> {
        public Serializer() {
            super(FieldId.class);
        }

        @Override
        public void serialize(FieldId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeNumber(generator, id.value());
        }
    }

    public static final class Deserializer extends StdDeserializer<FieldId> {
        public Deserializer() {
            super(FieldId.class);
        }

        @Override
        public FieldId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
            return new FieldId(UnsignedLongJson.read(parser, "FieldId"));
        }
    }

    public static final class KeySerializer extends com.fasterxml.jackson.databind.ser.std.StdSerializer<FieldId> {
        public KeySerializer() {
            super(FieldId.class);
        }

        @Override
        public void serialize(FieldId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeFieldName(generator, id.value());
        }
    }

    public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
        @Override
        public FieldId deserializeKey(String key, DeserializationContext context) {
            return new FieldId(UnsignedLongJson.parseUnsigned(key));
        }
    }
}
