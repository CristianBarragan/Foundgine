package com.foundgine.providers.aot;

import java.lang.annotation.*;

@Retention(RetentionPolicy.RUNTIME)
@Target({ ElementType.FIELD, ElementType.METHOD })
public @interface FoundgineConnection {
	Class<?> target() default Object.class;

	long id() default 0;

	String name() default "";
}
