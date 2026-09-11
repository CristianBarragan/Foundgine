package com.foundgine.core.semantic.metadata;

import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.databind.DeserializationContext;
import com.fasterxml.jackson.databind.KeyDeserializer;
import com.fasterxml.jackson.databind.SerializerProvider;
import com.fasterxml.jackson.databind.annotation.JsonDeserialize;
import com.fasterxml.jackson.databind.annotation.JsonSerialize;
import com.fasterxml.jackson.databind.deser.std.StdDeserializer;
import com.fasterxml.jackson.databind.ser.std.StdSerializer;
import com.foundgine.core.abstractions.SemanticIdentity;
import com.foundgine.core.abstractions.UnsignedLongJson;
import java.io.IOException;

/** Identity of a physical storage entity such as a database table. */
@JsonSerialize(using = StorageEntityId.Serializer.class, keyUsing = StorageEntityId.KeySerializer.class)
@JsonDeserialize(using = StorageEntityId.Deserializer.class, keyUsing = StorageEntityId.KeyDeserializer.class)
public record StorageEntityId(long value) {
    public static StorageEntityId create(String storageName) {
        return new StorageEntityId(SemanticIdentity.hash(SemanticIdentity.tableKey(storageName)));
    }
    public static final class Serializer extends StdSerializer<StorageEntityId> {
        public Serializer() { super(StorageEntityId.class); }
        @Override public void serialize(StorageEntityId id, JsonGenerator g, SerializerProvider p) throws IOException {
            UnsignedLongJson.writeNumber(g, id.value());
        }
    }
    public static final class Deserializer extends StdDeserializer<StorageEntityId> {
        public Deserializer() { super(StorageEntityId.class); }
        @Override public StorageEntityId deserialize(JsonParser p, DeserializationContext c) throws IOException {
            return new StorageEntityId(UnsignedLongJson.read(p, "StorageEntityId"));
        }
    }
    public static final class KeySerializer extends StdSerializer<StorageEntityId> {
        public KeySerializer() { super(StorageEntityId.class); }
        @Override public void serialize(StorageEntityId id, JsonGenerator g, SerializerProvider p) throws IOException {
            UnsignedLongJson.writeFieldName(g, id.value());
        }
    }
    public static final class KeyDeserializer extends KeyDeserializer {
        @Override public StorageEntityId deserializeKey(String key, DeserializationContext c) {
            return new StorageEntityId(UnsignedLongJson.parseUnsigned(key));
        }
    }
}
