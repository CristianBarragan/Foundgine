package com.foundgine.providers.aot;

import java.lang.annotation.*;

@Retention(RetentionPolicy.RUNTIME)
@Target({ ElementType.FIELD, ElementType.METHOD })
public @interface FoundgineSemanticDimension {
	String value();
}
