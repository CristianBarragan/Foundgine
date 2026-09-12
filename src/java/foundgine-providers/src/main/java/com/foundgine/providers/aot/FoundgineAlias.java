package com.foundgine.providers.aot;

import java.lang.annotation.*;

@Retention(RetentionPolicy.RUNTIME)
@Target({ ElementType.TYPE, ElementType.FIELD })
@Repeatable(FoundgineAliases.class)
public @interface FoundgineAlias {
	String value();

	int weight() default 100;
}
