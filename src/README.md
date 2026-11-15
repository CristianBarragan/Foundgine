# Source layout

Foundgine keeps each language implementation under its own language-specific
source root:

```text
src/
├── csharp/                         # .NET 10 implementation
│   ├── Foundgine.Core/
│   ├── Foundgine.Runtime/
│   ├── Foundgine.Providers/
│   ├── Foundgine.Providers.AgentFramework/  # separate packable project; not in the solution yet
│   └── Foundgine.Extensions/
├── java/                           # Java 27 Maven reactor
│   ├── pom.xml
│   ├── foundgine-core/
│   ├── foundgine-runtime/
│   ├── foundgine-providers/
│   ├── foundgine-extensions/
│   ├── foundgine-migrations/
│   ├── foundgine-spring/            # optional integration; dependency/runtime verification pending
│   ├── foundgine-langchain4j/       # optional integration; dependency/runtime verification pending
│   ├── foundgine-aot-generator/     # build-time module; not published separately
│   ├── security/foundgine-redteam/
│   └── samples/
├── conformance/                    # shared .NET/Java fixtures and generators
├── typescript/                     # @foundgine/typescript transport client
├── python/                         # foundgine-sdk transport client
└── wasm/                            # Rust/WebAssembly transport client
```

## Why this layout?

`src/` is the common source boundary for Foundgine. The language is explicit
at the first level, so the repository can grow a complete Java implementation
without mixing Java and C# projects in the same directory.

- `src/csharp/` contains the existing .NET solution projects.
- `src/java/` contains the Maven reactor, server modules, optional integrations, migration tooling, security module and samples.
- `src/conformance/` contains shared cross-language fixtures and their generators; see its [README](conformance/README.md).
- `Foundgine.Providers.AgentFramework` is a separate packable C# project and is not yet included in `Foundgine.sln`; its source and tests have not been build-verified.
- C# tests remain under `src/csharp/tests/`.
- Java tests follow Maven convention inside each Java module under
  `src/test/java/`.

The two implementations can therefore evolve side-by-side while preserving
the same conceptual Foundgine module boundaries.
