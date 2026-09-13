package com.foundgine.providers.aot;

import java.lang.annotation.*;

@Retention(RetentionPolicy.RUNTIME)
@Target({ ElementType.FIELD, ElementType.METHOD })
public @interface FoundgineRelationship {
	Class<?> target();

	String foreignKey();

	String principalKey();

	long id() default 0;

	String name() default "";
}
