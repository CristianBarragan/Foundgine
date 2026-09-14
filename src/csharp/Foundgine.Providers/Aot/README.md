# Foundgine.Providers.Aot

`Foundgine.Providers.Aot` is the application-facing declaration and runtime-support code for Foundgine's AOT metadata pipeline. It lives inside `Foundgine.Providers` — the former standalone `Foundgine.Experimental` package was deleted in v2 and this content merged in directly.

## What is in this folder

### Declaration attributes

Attributes for:

- entities and fields;
- relationships;
- semantic models;
- connections and connection maps;
- model/entity maps;
- conversions;
- aliases;
- authorization;
- semantic dimensions;
- events.

### Generated/runtime helpers

- `GeneratedSemanticField`
- `GeneratedSemanticFieldExtensions`
- `IMetadataSource`
- AOT package marker/support types.

`Foundgine.Providers.Aot` is paired with the `Foundgine.Providers.Aot.Generator` Roslyn source generator (still under its historical project name, built as its own netstandard2.0 assembly at `src/csharp/Foundgine.Providers/Foundgine.Providers.Aot.Generator/` and referenced as an analyzer, since source generators cannot be merged into a regular library assembly). The generator performs compile-time discovery and source generation; this folder supplies the public declarations and runtime support.

## Packaging

`Foundgine.Providers` intentionally depends on `Foundgine.Core.Semantic.Metadata` and references `Foundgine.Providers.Aot.Generator.dll` as a Roslyn analyzer. The generator project is build-only and is not a separate NuGet package.

## Use

Any NuGet consumer that references `Foundgine.Providers` receives these attributes/helpers **and** the generator automatically, because the generator DLL is packed under `analyzers/dotnet/cs/` in the same NuGet package. No second package or analyzer reference is required.

Only projects building directly from this repository via `ProjectReference` may need an explicit analyzer `ProjectReference` to `Foundgine.Providers.Aot.Generator.csproj`, because project-to-project analyzer references are not transitive. This is a repository-development concern, not a NuGet consumer requirement.

Use this when stable application/domain metadata should be generated at compile time rather than discovered through runtime reflection.
