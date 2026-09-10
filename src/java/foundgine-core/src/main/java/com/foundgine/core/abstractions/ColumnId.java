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
 * Port of {@code Foundgine.Core.Abstractions.ColumnId} (a C# {@code readonly record struct}).
 *
 * <p>Stable identity of a physical column.
 *
 * <p>{@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C# {@code ulong}.
 * The nested (de)serializers cover both the value position (equivalent to
 * {@code Read}/{@code Write} in the C# converter) and use as a {@code Map}
 * key (equivalent to {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = ColumnId.Serializer.class, keyUsing = ColumnId.KeySerializer.class)
@JsonDeserialize(using = ColumnId.Deserializer.class, keyUsing = ColumnId.KeyDeserializer.class)
public record ColumnId(long value) implements SemanticId {

    public static ColumnId create(String storageName, String columnName) {
        return new ColumnId(SemanticIdentity.hash(SemanticIdentity.columnKey(storageName, columnName)));
    }

    public static final class Serializer extends StdSerializer<ColumnId> {
        public Serializer() {
            super(ColumnId.class);
        }

        @Override
        public void serialize(ColumnId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeNumber(generator, id.value());
        }
    }

    public static final class Deserializer extends StdDeserializer<ColumnId> {
        public Deserializer() {
            super(ColumnId.class);
        }

        @Override
        public ColumnId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
            return new ColumnId(UnsignedLongJson.read(parser, "ColumnId"));
        }
    }

    public static final class KeySerializer extends com.fasterxml.jackson.databind.ser.std.StdSerializer<ColumnId> {
        public KeySerializer() {
            super(ColumnId.class);
        }

        @Override
        public void serialize(ColumnId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeFieldName(generator, id.value());
        }
    }

    public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
        @Override
        public ColumnId deserializeKey(String key, DeserializationContext context) {
            return new ColumnId(UnsignedLongJson.parseUnsigned(key));
        }
    }
}
