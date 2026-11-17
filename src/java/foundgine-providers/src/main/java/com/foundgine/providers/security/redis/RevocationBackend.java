package com.foundgine.providers.security.redis;

import java.util.List;

/** Minimal Redis-protocol EVAL surface required by distributed warrant revocation. */
@FunctionalInterface
public interface RevocationBackend {
    List<String> eval(String script, List<String> keys, List<String> arguments);
}
