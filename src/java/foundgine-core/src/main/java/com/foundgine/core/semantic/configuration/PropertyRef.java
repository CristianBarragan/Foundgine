package com.foundgine.core.semantic.configuration;

import java.io.Serializable;
import java.util.function.Function;

/**
 * A typed, serializable property reference such as {@code Customer::getName} or the record
 * accessor {@code Customer::name}. Serializable so the configuration API can read the
 * property name from the method reference without any string literals.
 *
 * <p>Only method references are supported. A lambda such as {@code c -> c.getName()} is
 * rejected with a clear message because its target method cannot be recovered.
 */
@FunctionalInterface
public interface PropertyRef<T, R> extends Function<T, R>, Serializable {}
