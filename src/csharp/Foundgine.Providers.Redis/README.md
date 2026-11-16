# Foundgine.Providers.Redis

Shared, atomic warrant replay protection for horizontally scaled Foundgine hosts. Implements
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
