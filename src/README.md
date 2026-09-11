# Source layout

Foundgine keeps each language implementation under its own language-specific
source root:

```text
src/
├── csharp/                 # .NET / C# implementation
│   ├── Foundgine.Core/
│   ├── Foundgine.Runtime/
│   ├── Foundgine.Providers/
│   └── Foundgine.Extensions/
│
└── java/                   # Java implementation
    ├── pom.xml
    ├── PORTING_NOTES.md
    └── foundgine-core/
        ├── pom.xml
        └── src/
            ├── main/java/
            └── test/java/
```

## Why this layout?

`src/` is the common source boundary for Foundgine. The language is explicit
at the first level, so the repository can grow a complete Java implementation
without mixing Java and C# projects in the same directory.

- `src/csharp/` contains the existing .NET solution projects.
- `src/java/` contains the Maven reactor and Java modules.
- C# tests remain under `src/csharp/tests/`.
- Java tests follow Maven convention inside each Java module under
  `src/test/java/`.

The two implementations can therefore evolve side-by-side while preserving
the same conceptual Foundgine module boundaries.
