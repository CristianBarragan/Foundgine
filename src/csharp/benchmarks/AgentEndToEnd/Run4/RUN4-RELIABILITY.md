# Run 4 reliability

Run 4 owns its Docker Compose project and PostgreSQL volume. Startup now uses `docker compose up -d --wait` for PostgreSQL and both API services, then verifies `/health/ready` before benchmarking. This removes the previous race where `docker compose exec postgres ...` could be attempted before the container was running.

The MCP server uses Streamable HTTP stateless mode and the benchmark sends the `2026-07-28` protocol header.
