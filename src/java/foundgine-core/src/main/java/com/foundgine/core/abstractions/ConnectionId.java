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
 * Port of {@code Foundgine.Core.Abstractions.ConnectionId} (a C#
 * {@code readonly record struct}).
 *
 * <p>
 * Stable identity of a semantic model connection.
 *
 * <p>
 * {@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C#
 * {@code ulong}. The nested (de)serializers cover both the value position
 * (equivalent to {@code Read}/{@code Write} in the C# converter) and use as a
 * {@code Map} key (equivalent to
 * {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = ConnectionId.Serializer.class, keyUsing = ConnectionId.KeySerializer.class)
@JsonDeserialize(using = ConnectionId.Deserializer.class, keyUsing = ConnectionId.KeyDeserializer.class)
public record ConnectionId(long value) implements SemanticId {

	public static ConnectionId create(String semanticModelName, String semanticConnectionName) {
		return new ConnectionId(
				SemanticIdentity.hash(SemanticIdentity.connectionKey(semanticModelName, semanticConnectionName)));
	}

	public static final class Serializer extends StdSerializer<ConnectionId> {
		public Serializer() {
			super(ConnectionId.class);
		}

		@Override
		public void serialize(ConnectionId id, JsonGenerator generator, SerializerProvider provider)
				throws IOException {
			UnsignedLongJson.writeNumber(generator, id.value());
		}
	}

	public static final class Deserializer extends StdDeserializer<ConnectionId> {
		public Deserializer() {
			super(ConnectionId.class);
		}

		@Override
		public ConnectionId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
			return new ConnectionId(UnsignedLongJson.read(parser, "ConnectionId"));
		}
	}

	public static final class KeySerializer extends com.fasterxml.jackson.databind.ser.std.StdSerializer<ConnectionId> {
		public KeySerializer() {
			super(ConnectionId.class);
		}

		@Override
		public void serialize(ConnectionId id, JsonGenerator generator, SerializerProvider provider)
				throws IOException {
			UnsignedLongJson.writeFieldName(generator, id.value());
		}
	}

	public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
		@Override
		public ConnectionId deserializeKey(String key, DeserializationContext context) {
			return new ConnectionId(UnsignedLongJson.parseUnsigned(key));
		}
	}
}
