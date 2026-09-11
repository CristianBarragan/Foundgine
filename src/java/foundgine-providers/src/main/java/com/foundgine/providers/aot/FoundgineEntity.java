package com.foundgine.providers.aot;
import java.lang.annotation.*;
@Retention(RetentionPolicy.RUNTIME) @Target(ElementType.TYPE)
public @interface FoundgineEntity { String name() default ""; long id() default 0; }
