package com.foundgine.core.semantic;

import java.lang.annotation.*;

@Retention(RetentionPolicy.RUNTIME)
@Target({ ElementType.TYPE, ElementType.METHOD, ElementType.FIELD })
public @interface SemanticPolicyAttribute {
	String name();
}