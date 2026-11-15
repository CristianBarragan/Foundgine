# Foundgine.Providers — Redis integration

Shared Redis-compatible state for horizontally scaled Foundgine hosts. Implements
`ISecurityWarrantReplayStore` with one `SET key 1 NX PX ttl` per consumed warrant, so exactly one host wins each
`(warrantId, nonce)` pair. Works with Redis, Valkey and Microsoft Garnet (Redis protocol, built on FASTER).

```csharp
var mux = await ConnectionMultiplexer.ConnectAsync("garnet:6379,syncTimeout=1000");
builder.Services.AddFoundgine(o =>
{
    o.WarrantReplayStore = new RedisSecurityWarrantReplayStore(new StackExchangeReplayBackend(mux));
    // ...
});
```

- **Fails closed.** An unreachable server or error reply throws `InvalidOperationException`; the request is denied.
  Set `syncTimeout` so a hung server cannot block requests, and run the server replicated if an outage must not stop traffic.
- **Bounded storage.** Entries expire at warrant expiry plus one minute of clock-skew grace.
- **No raw nonces stored.** Keys are `prefix + SHA-256(warrantId 0x1F nonce)`, identical to the file store and the Java
  `RedisSecurityWarrantReplayStore`. Use one `keyPrefix` per environment on a shared server.
- Persistence matters: a server that loses data on restart forgets consumed nonces. Enable AOF/persistence (or accept
  that the replay window reopens for warrants still inside their lifetime).


Install the consolidated provider package:

```bash
dotnet add package Foundgine.Providers
```


## Shared MCP sessions

`RedisFoundgineMcpSessionStore` implements `IFoundgineMcpSessionStore` for the MCP session manager. It stores one hash per session plus a sorted-set expiry index. Each add, touch, remove or expiry operation is a single Lua script, so replicas do not interleave read/modify/write operations. The default key prefix includes a Redis Cluster hash tag; retain a hash tag in custom prefixes. The Java `RedisMcpSessionStore` uses the same key layout so mixed C#/Java deployments can share sessions.

The session store fails closed when Redis is unreachable. The SSE replay buffer remains process-local; shared session state does not make SSE replay distributed.

## Tests

Unit tests use fake Redis backends. Live-server tests exercise the actual commands/scripts; start Redis from the repository root with `docker compose -f docker-compose.redis.yml up -d --wait redis`, set `FOUNDGINE_REDIS=127.0.0.1:56379`, then run `dotnet test tests/Foundgine.Redis.Tests/Foundgine.Redis.Tests.csproj --configuration Release` from `src/csharp`.
