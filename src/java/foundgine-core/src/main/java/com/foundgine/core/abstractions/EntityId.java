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
 * Port of {@code Foundgine.Core.Abstractions.EntityId} (a C#
 * {@code readonly record struct}).
 *
 * <p>
 * Stable identity of a semantic entity. The value is derived from the canonical
 * semantic entity name and is independent of declaration order, class metadata,
 * or registration order.
 *
 * <p>
 * {@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C#
 * {@code ulong}. The nested (de)serializers cover both the value position
 * (equivalent to {@code Read}/{@code Write} in the C# converter) and use as a
 * {@code Map} key (equivalent to
 * {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = EntityId.Serializer.class, keyUsing = EntityId.KeySerializer.class)
@JsonDeserialize(using = EntityId.Deserializer.class, keyUsing = EntityId.KeyDeserializer.class)
public record EntityId(long value) implements SemanticId {

	public static EntityId create(String semanticName) {
		return new EntityId(SemanticIdentity.hash(SemanticIdentity.entityKey(semanticName)));
	}

	public static final class Serializer extends StdSerializer<EntityId> {
		public Serializer() {
			super(EntityId.class);
		}

		@Override
		public void serialize(EntityId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
			UnsignedLongJson.writeNumber(generator, id.value());
		}
	}

	public static final class Deserializer extends StdDeserializer<EntityId> {
		public Deserializer() {
			super(EntityId.class);
		}

		@Override
		public EntityId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
			return new EntityId(UnsignedLongJson.read(parser, "EntityId"));
		}
	}

	public static final class KeySerializer extends com.fasterxml.jackson.databind.ser.std.StdSerializer<EntityId> {
		public KeySerializer() {
			super(EntityId.class);
		}

		@Override
		public void serialize(EntityId id, JsonGenerator generator, SerializerProvider provider) throws IOException {
			UnsignedLongJson.writeFieldName(generator, id.value());
		}
	}

	public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
		@Override
		public EntityId deserializeKey(String key, DeserializationContext context) {
			return new EntityId(UnsignedLongJson.parseUnsigned(key));
		}
	}
}
