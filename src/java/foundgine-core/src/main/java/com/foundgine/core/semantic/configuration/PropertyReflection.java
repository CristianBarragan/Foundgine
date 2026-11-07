package com.foundgine.core.semantic.configuration;

import java.lang.invoke.MethodHandleInfo;
import java.lang.invoke.SerializedLambda;
import java.lang.reflect.Method;
import java.lang.reflect.ParameterizedType;
import java.lang.reflect.Type;
import java.util.Objects;

/**
 * Reads member names and types out of property references. Uses the serialized form of the
 * method reference; the lambda is never invoked. This is reflective, so GraalVM native-image
 * builds need reflection/serialization configuration for configuration classes until
 * compile-time discovery (annotation processing) is added.
 */
final class PropertyReflection {
    private PropertyReflection() {}

    static MemberRef resolve(PropertyRef<?, ?> reference) {
        Objects.requireNonNull(reference, "property");

        SerializedLambda lambda = serialize(reference);
        int kind = lambda.getImplMethodKind();
        String methodName = lambda.getImplMethodName();

        boolean getterShaped =
                (kind == MethodHandleInfo.REF_invokeVirtual
                                || kind == MethodHandleInfo.REF_invokeInterface)
                        && lambda.getImplMethodSignature().startsWith("()")
                        && !methodName.startsWith("lambda$");
        if (!getterShaped) {
            throw new IllegalArgumentException(
                    "Property selectors must be getter method references such as Customer::getName"
                            + " (or a record accessor such as Customer::name); got "
                            + lambda.getImplClass()
                            + "."
                            + methodName
                            + ".");
        }

        Class<?> owner = load(lambda.getImplClass(), reference.getClass().getClassLoader());
        Method method = find(owner, methodName);
        return new MemberRef(
                propertyName(methodName), method.getReturnType(), method.getGenericReturnType());
    }

    static boolean isCollection(Class<?> type) {
        return type.isArray() || Iterable.class.isAssignableFrom(type);
    }

    /** Element type of a collection navigation, or null when it cannot be determined. */
    static Class<?> elementType(MemberRef member) {
        if (member.type().isArray()) return member.type().getComponentType();
        if (member.genericType() instanceof ParameterizedType parameterized) {
            Type[] arguments = parameterized.getActualTypeArguments();
            if (arguments.length == 1 && arguments[0] instanceof Class<?> element) return element;
        }
        return null;
    }

    static Class<?> box(Class<?> type) {
        if (!type.isPrimitive()) return type;
        if (type == int.class) return Integer.class;
        if (type == long.class) return Long.class;
        if (type == boolean.class) return Boolean.class;
        if (type == double.class) return Double.class;
        if (type == float.class) return Float.class;
        if (type == short.class) return Short.class;
        if (type == byte.class) return Byte.class;
        if (type == char.class) return Character.class;
        return type;
    }

    static String propertyName(String methodName) {
        String stripped = methodName;
        if (methodName.length() > 3
                && methodName.startsWith("get")
                && Character.isUpperCase(methodName.charAt(3))) {
            stripped = methodName.substring(3);
        } else if (methodName.length() > 2
                && methodName.startsWith("is")
                && Character.isUpperCase(methodName.charAt(2))) {
            stripped = methodName.substring(2);
        } else {
            return methodName; // record accessor: already the component name
        }

        // Same rule as java.beans.Introspector.decapitalize: keep acronyms such as "URL".
        if (stripped.length() > 1
                && Character.isUpperCase(stripped.charAt(0))
                && Character.isUpperCase(stripped.charAt(1))) {
            return stripped;
        }
        return Character.toLowerCase(stripped.charAt(0)) + stripped.substring(1);
    }

    private static SerializedLambda serialize(PropertyRef<?, ?> reference) {
        try {
            Method writeReplace = reference.getClass().getDeclaredMethod("writeReplace");
            writeReplace.setAccessible(true);
            return (SerializedLambda) writeReplace.invoke(reference);
        } catch (ReflectiveOperationException | RuntimeException e) {
            throw new IllegalArgumentException(
                    "Property selectors must be method references such as Customer::getName.", e);
        }
    }

    private static Class<?> load(String internalName, ClassLoader loader) {
        String name = internalName.replace('/', '.');
        try {
            return Class.forName(name, false, loader != null ? loader : ClassLoader.getSystemClassLoader());
        } catch (ClassNotFoundException e) {
            throw new IllegalArgumentException("Cannot load " + name + " for a property reference.", e);
        }
    }

    private static Method find(Class<?> owner, String name) {
        try {
            return owner.getMethod(name);
        } catch (NoSuchMethodException publicMiss) {
            for (Class<?> c = owner; c != null; c = c.getSuperclass()) {
                try {
                    return c.getDeclaredMethod(name);
                } catch (NoSuchMethodException ignored) {
                    // keep walking up
                }
            }
            throw new IllegalArgumentException(
                    "No no-argument method " + name + " on " + owner.getName() + ".", publicMiss);
        }
    }
}
