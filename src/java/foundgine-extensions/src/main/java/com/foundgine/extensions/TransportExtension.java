package com.foundgine.extensions;

/** Marker for extensions that translate an external transport into Foundgine intent. */
public interface TransportExtension extends FoundgineExtension {
    String transport();
}
