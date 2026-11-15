package com.foundgine.providers.tools.mcp.redis;

import java.util.List;

/**
 * The one operation the shared MCP session store needs from a Redis-protocol server (Redis, Valkey, Microsoft Garnet or any
 * compatible service): run a Lua script atomically ({@code EVAL}).
 *
 * <p>Every script the store runs returns a flat array of strings, so an implementation only has to map the reply to a
 * {@code List<String>} (integers and bulk strings both converted with {@code String.valueOf}). An unreachable server or an
 * error reply must throw a {@link RuntimeException}; the store surfaces it as a failed request, never as a valid session.
 */
@FunctionalInterface
public interface RedisScriptBackend {
    List<String> eval(String script, List<String> keys, List<String> args);
}
