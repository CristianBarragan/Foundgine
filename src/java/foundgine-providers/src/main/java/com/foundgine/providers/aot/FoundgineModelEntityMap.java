package com.foundgine.providers.aot;
import java.lang.annotation.*;
@Retention(RetentionPolicy.RUNTIME) @Target(ElementType.TYPE) @Repeatable(FoundgineModelEntityMaps.class)
public @interface FoundgineModelEntityMap { Class<?> model(); Class<?> entity(); }
