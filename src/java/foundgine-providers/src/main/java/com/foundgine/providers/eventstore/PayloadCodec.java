package com.foundgine.providers.eventstore;

/** Caller-supplied, reflection-free payload serializer (the Java counterpart of a source-generated JsonTypeInfo). */
public interface PayloadCodec<T> {
    String toJson(T payload);
    T fromJson(String json);
}
