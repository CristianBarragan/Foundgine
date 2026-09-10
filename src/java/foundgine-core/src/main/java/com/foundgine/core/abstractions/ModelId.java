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
 * Port of {@code Foundgine.Core.Abstractions.ModelId} (a C# {@code readonly record struct}).
 *
 * <p>Stable identity of a semantic model.
 *
 * <p>{@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C# {@code ulong}.
 * The nested (de)serializers cover both the value position (equivalent to
 * {@code Read}/{@code Write} in the C# converter) and use as a {@code Map}
 * key (equivalent to {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = ModelId.Serializer.class, keyUsing = ModelId.KeySerializer.class)
@JsonDeserialize(using = ModelId.Deserializer.class, keyUsing = ModelId.KeyDeserializer.class)
public record ModelId(long value) implements SemanticId {

    public static ModelId create(String semanticName) {
        return new ModelId(SemanticIdentity.hash(SemanticIdentity.modelKey(semanticName)));
    }

    public static final class Serializer extends StdSerializer<ModelId> {
        public Serializer() {
            super(ModelId.class);
        }

        @Override
        public void serialize(ModelId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeNumber(generator, id.value());
        }
    }

    public static final class Deserializer extends StdDeserializer<ModelId> {
        public Deserializer() {
            super(ModelId.class);
        }

        @Override
        public ModelId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
            return new ModelId(UnsignedLongJson.read(parser, "ModelId"));
        }
    }

    public static final class KeySerializer extends com.fasterxml.jackson.databind.ser.std.StdSerializer<ModelId> {
        public KeySerializer() {
            super(ModelId.class);
        }

        @Override
        public void serialize(ModelId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeFieldName(generator, id.value());
        }
    }

    public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
        @Override
        public ModelId deserializeKey(String key, DeserializationContext context) {
            return new ModelId(UnsignedLongJson.parseUnsigned(key));
        }
    }
}
