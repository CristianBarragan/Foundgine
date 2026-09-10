package com.foundgine.providers.aot;
import java.lang.annotation.*;
@Retention(RetentionPolicy.RUNTIME) @Target(ElementType.TYPE) @Repeatable(FoundgineConnectionMaps.class)
public @interface FoundgineConnectionMap { Class<?> model(); String connectionMember(); Class<?> entity(); }
