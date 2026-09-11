package com.foundgine.providers.aot;
import java.lang.annotation.*;
@Retention(RetentionPolicy.RUNTIME) @Target(ElementType.TYPE)
public @interface FoundgineConnectionMaps { FoundgineConnectionMap[] value(); }
