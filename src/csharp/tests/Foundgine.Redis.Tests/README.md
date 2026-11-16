# Redis provider integration tests

The unit tests use in-memory fake backends. Live-server tests exercise the actual Redis Lua scripts and `SET NX PX` commands over RESP2.

Start Redis using the repository's Docker Compose configuration from the repository root:

```bash
docker compose -p foundgine-redis -f docker-compose.redis.yml up -d --wait redis
```

In another terminal, from `src/csharp`:

```bash
# Linux/macOS
export FOUNDGINE_REDIS=127.0.0.1:56379
# PowerShell
$env:FOUNDGINE_REDIS = '127.0.0.1:56379'

dotnet test tests/Foundgine.Redis.Tests/Foundgine.Redis.Tests.csproj --configuration Release
```

When the live tests have finished, stop Redis from the repository root:

```bash
docker compose -p foundgine-redis -f docker-compose.redis.yml down --remove-orphans
```

`FOUNDGINE_REDIS` accepts `host:port` or `redis://[:password@]host:port/database`. When it is absent, only the live-server tests are reported as skipped; the ordinary fake-backend unit tests still run. The tests use unique key prefixes and do not flush or delete unrelated Redis data.
