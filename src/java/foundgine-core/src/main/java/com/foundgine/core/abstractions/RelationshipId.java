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
 * Port of {@code Foundgine.Core.Abstractions.RelationshipId} (a C#
 * {@code readonly record struct}).
 *
 * <p>
 * Stable semantic identity for a relationship.
 *
 * <p>
 * {@code value} is the identity, interpreted as an <b>unsigned</b> 64-bit
 * integer (see {@link UnsignedLongJson}), matching the wrapped C#
 * {@code ulong}. The nested (de)serializers cover both the value position
 * (equivalent to {@code Read}/{@code Write} in the C# converter) and use as a
 * {@code Map} key (equivalent to
 * {@code ReadAsPropertyName}/{@code WriteAsPropertyName}).
 */
@JsonSerialize(using = RelationshipId.Serializer.class, keyUsing = RelationshipId.KeySerializer.class)
@JsonDeserialize(using = RelationshipId.Deserializer.class, keyUsing = RelationshipId.KeyDeserializer.class)
public record RelationshipId(long value) implements SemanticId {

	public static RelationshipId create(String semanticEntityName, String semanticRelationshipName) {
		return new RelationshipId(
				SemanticIdentity.hash(SemanticIdentity.relationshipKey(semanticEntityName, semanticRelationshipName)));
	}

	public static final class Serializer extends StdSerializer<RelationshipId> {
		public Serializer() {
			super(RelationshipId.class);
		}

		@Override
		public void serialize(RelationshipId id, JsonGenerator generator, SerializerProvider provider)
				throws IOException {
			UnsignedLongJson.writeNumber(generator, id.value());
		}
	}

	public static final class Deserializer extends StdDeserializer<RelationshipId> {
		public Deserializer() {
			super(RelationshipId.class);
		}

		@Override
		public RelationshipId deserialize(JsonParser parser, DeserializationContext context) throws IOException {
			return new RelationshipId(UnsignedLongJson.read(parser, "RelationshipId"));
		}
	}

	public static final class KeySerializer
			extends com.fasterxml.jackson.databind.ser.std.StdSerializer<RelationshipId> {
		public KeySerializer() {
			super(RelationshipId.class);
		}

		@Override
		public void serialize(RelationshipId id, JsonGenerator generator, SerializerProvider provider)
				throws IOException {
			UnsignedLongJson.writeFieldName(generator, id.value());
		}
	}

	public static final class KeyDeserializer extends com.fasterxml.jackson.databind.KeyDeserializer {
		@Override
		public RelationshipId deserializeKey(String key, DeserializationContext context) {
			return new RelationshipId(UnsignedLongJson.parseUnsigned(key));
		}
	}
}
