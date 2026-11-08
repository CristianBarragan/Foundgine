# Foundgine.Providers samples

- [Gateway.MinimalApi](Gateway.MinimalApi/README.md) — one ASP.NET Core Minimal API host composing the InMemory, SQL/PostgreSQL, Elasticsearch, pgvector, EventStore, Outbox, MCP, AI and Gateway provider contracts.

The Minimal API sample demonstrates the provider interfaces together through dependency injection. Infrastructure-backed integrations are opt-in through configuration; the application still supplies its generated metadata, Runtime facade, authorization context, embedding model and chat client.
