package com.foundgine.providers.aot;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * Marks a concrete persistence class for compile-time ORM access-model generation.
 *
 * <p>{@link #entity()} names the semantic entity the class backs. When empty, the {@code @FoundgineEntity}
 * name is used if present, else the class simple name with a trailing {@code Entity} removed
 * ({@code SupplierEntity -> Supplier}).
 */
@Retention(RetentionPolicy.SOURCE)
@Target(ElementType.TYPE)
public @interface FoundgineOrmEntity {
    String entity() default "";
}
