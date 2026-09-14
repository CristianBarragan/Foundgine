package com.foundgine.extensions;

/**
 * Optional integration boundary. Extensions adapt external frameworks or transports
 * to Foundgine without moving transport-specific concerns into Core or Runtime.
 */
public interface FoundgineExtension {
    String id();
    default String version() { return "1"; }
}
