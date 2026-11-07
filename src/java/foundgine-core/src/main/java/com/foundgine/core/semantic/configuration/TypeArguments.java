package com.foundgine.core.semantic.configuration;

import java.lang.reflect.ParameterizedType;
import java.lang.reflect.Type;

/** Reads the concrete type arguments a configuration class passed to its generic base class. */
final class TypeArguments {
    private TypeArguments() {}

    static Class<?>[] resolve(Class<?> subclass, Class<?> base) {
        for (Class<?> c = subclass; c != null && c != Object.class; c = c.getSuperclass()) {
            if (c.getGenericSuperclass() instanceof ParameterizedType parameterized
                    && parameterized.getRawType() == base) {
                Type[] arguments = parameterized.getActualTypeArguments();
                Class<?>[] resolved = new Class<?>[arguments.length];
                for (int i = 0; i < arguments.length; i++) {
                    if (arguments[i] instanceof Class<?> type) resolved[i] = type;
                    else if (arguments[i] instanceof ParameterizedType nested
                            && nested.getRawType() instanceof Class<?> raw) resolved[i] = raw;
                    else throw unresolved(subclass, base);
                }
                return resolved;
            }
        }
        throw unresolved(subclass, base);
    }

    private static IllegalStateException unresolved(Class<?> subclass, Class<?> base) {
        return new IllegalStateException(
                subclass.getName()
                        + " must extend "
                        + base.getSimpleName()
                        + " with concrete type arguments (for example "
                        + base.getSimpleName()
                        + "<Customer>), or pass the Class tokens to the base constructor.");
    }
}
