package com.foundgine.providers.aot;

import java.lang.annotation.*;

@Retention(RetentionPolicy.RUNTIME)
@Target({ ElementType.FIELD, ElementType.METHOD })
public @interface FoundgineField {
	String name() default "";

	String storageName() default "";

	long id() default 0;

	long columnId() default 0;

	boolean isPrimaryKey() default false;

	boolean index() default false;
}
