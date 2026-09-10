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
 * Port of {@code Foundgine.Core.Abstractions.AuthorizationId} (a C# {@code readonly record struct}).
 *
 * <p>Stable identifier for an AOT authorization predicate.
 *
 * <p>{@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C# {@code ulong}.
 * The nested (de)serializers cover both the value position (equivalent to
 * {@code Read}/{@code Write} in the C# converter) and use as a {@code Map}
 * key (equivalent to {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = AuthorizationId.Serializer.class, keyUsing = AuthorizationId.KeySerializer.class)
@JsonDeserialize(using = AuthorizationId.Deserializer.class, keyUsing = AuthorizationId.KeyDeserializer.class)
public record AuthorizationId(long value) implements SemanticId {

    public static AuthorizationId create(String declaringType, String authorizationName) {
        return new AuthorizationId(SemanticIdentity.hash(SemanticIdentity.authorizationKey(declaringType, authorizationName)));
    }

    public static final class Serializer extends StdSerializer<AuthorizationId> {
        public Serializer() {
            super(AuthorizationId.class);
        }

        @Override
        public void serialize(AuthorizationId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeNumber(generator, id.value());
        }
    }

    public static final class Deserializer extends StdDeserializer<AuthorizationId> {
        public Deserializer() {
            super(AuthorizationId.class);
        }

        @Override
        public AuthorizationId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
            return new AuthorizationId(UnsignedLongJson.read(parser, "AuthorizationId"));
        }
    }

    public static final class KeySerializer extends com.fasterxml.jackson.databind.ser.std.StdSerializer<AuthorizationId> {
        public KeySerializer() {
            super(AuthorizationId.class);
        }

        @Override
        public void serialize(AuthorizationId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
            UnsignedLongJson.writeFieldName(generator, id.value());
        }
    }

    public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
        @Override
        public AuthorizationId deserializeKey(String key, DeserializationContext context) {
            return new AuthorizationId(UnsignedLongJson.parseUnsigned(key));
        }
    }
}
