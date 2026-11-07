package com.foundgine.core.semantic.configuration;

import java.lang.reflect.Type;

/** A property selected by a {@link PropertyRef}: its name and declared type. */
record MemberRef(String name, Class<?> type, Type genericType) {}
