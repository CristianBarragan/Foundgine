package com.foundgine.providers.storage.orm;

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.util.Objects;

/**
 * Reflection-based attribute access for mutations. Prefers JavaBean getters/setters and falls
 * back to the field. A read-only attribute (no setter, final field) is reported by {@link
 * #canSet} so a mutation touching it is refused at validation time.
 */
public final class OrmEntityAccess {
    private OrmEntityAccess() {}

    public static boolean canCreate(Class<?> type) {
        try {
            type.getDeclaredConstructor();
            return !Modifier.isAbstract(type.getModifiers());
        } catch (NoSuchMethodException e) {
            return false;
        }
    }

    public static Object newInstance(Class<?> type) {
        try {
            Constructor<?> c = type.getDeclaredConstructor();
            c.setAccessible(true);
            return c.newInstance();
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException(
                    "Cannot create '" + type.getName() + "' (needs a no-arg constructor).", e);
        }
    }

    public static boolean canSet(Class<?> type, String name) {
        return setter(type, name) != null
                || (field(type, name) != null && !Modifier.isFinal(field(type, name).getModifiers()));
    }

    public static Object get(Object entity, String name) {
        Objects.requireNonNull(entity, "entity");
        try {
            Method getter = getter(entity.getClass(), name);
            if (getter != null) {
                getter.setAccessible(true);
                return getter.invoke(entity);
            }
            Field f = field(entity.getClass(), name);
            if (f == null) throw new IllegalStateException("No attribute '" + name + "' on " + entity.getClass().getName());
            f.setAccessible(true);
            return f.get(entity);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException("Cannot read '" + name + "'.", e);
        }
    }

    public static void set(Object entity, String name, Object value) {
        try {
            Method setter = setter(entity.getClass(), name);
            if (setter != null) {
                setter.setAccessible(true);
                setter.invoke(entity, value);
                return;
            }
            Field f = field(entity.getClass(), name);
            if (f == null || Modifier.isFinal(f.getModifiers())) {
                throw new IllegalStateException("Attribute '" + name + "' is read-only.");
            }
            f.setAccessible(true);
            f.set(entity, value);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException("Cannot write '" + name + "'.", e);
        }
    }

    private static String cap(String n) {
        return Character.toUpperCase(n.charAt(0)) + n.substring(1);
    }

    private static Method getter(Class<?> type, String name) {
        for (String candidate : new String[] {"get" + cap(name), "is" + cap(name)}) {
            try {
                Method m = type.getMethod(candidate);
                if (m.getReturnType() != void.class) return m;
            } catch (NoSuchMethodException ignored) {
                // try next
            }
        }
        return null;
    }

    private static Method setter(Class<?> type, String name) {
        String target = "set" + cap(name);
        for (Method m : type.getMethods()) {
            if (m.getName().equals(target) && m.getParameterCount() == 1) return m;
        }
        return null;
    }

    private static Field field(Class<?> type, String name) {
        for (Class<?> c = type; c != null && c != Object.class; c = c.getSuperclass()) {
            try {
                return c.getDeclaredField(name);
            } catch (NoSuchFieldException ignored) {
                // walk up
            }
        }
        return null;
    }
}
