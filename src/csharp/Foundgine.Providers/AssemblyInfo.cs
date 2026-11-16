using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Foundgine.Providers.Tests")]
[assembly: InternalsVisibleTo("Foundgine.E2E.Tests")]
[assembly: InternalsVisibleTo("Foundgine.Postgres.Vector.Tests")]
[assembly: InternalsVisibleTo("Foundgine.Gateway.Tests")]
[assembly: InternalsVisibleTo("Foundgine.EventStore.Tests")]
// Internal MCP helpers (MutationJsonAdapter) shared with the Agent Framework adapter, which used to be compiled into this assembly.
[assembly: InternalsVisibleTo("Foundgine.Providers.AgentFramework")]
