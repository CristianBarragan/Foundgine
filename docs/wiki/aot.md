# Ahead-of-time compilation and generated metadata

## Purpose

AOT support reduces reliance on runtime reflection and dynamic code generation. Foundgine uses build-time metadata generation so entity and semantic metadata can be available as ordinary generated source at compile time.

## Why it matters

Reflection-heavy discovery can be difficult for trimming and Native AOT because the linker cannot always infer which members must remain. A source generator or annotation processor makes the required metadata explicit and deterministic before the application runs.

## .NET approach

The C# provider package contains AOT attributes and metadata contracts, while the generator emits metadata source from annotated application types. Generated output should be deterministic, compile-time visible, and free of runtime reflection requirements for the generated path. The generator is shipped with the provider package as an analyzer asset according to the package layout.

## Java approach

The Java AOT generator is an annotation processor. It processes Foundgine annotations and emits generated metadata classes during compilation. Java's annotation processing is build-time code generation; it should not be confused with GraalVM native-image configuration or runtime reflection. Native-image compatibility still depends on the application's complete dependency graph and any external libraries.

## Metadata lifecycle

1. Annotate or otherwise declare supported application types.
2. Run the compiler/annotation processor.
3. Generate stable metadata and semantic identities.
4. Compile generated source with the application.
5. Register the generated metadata source with Foundgine.
6. Validate that runtime behavior uses the generated metadata path.

## Design constraints

- Keep generated code out of the core semantic contracts.
- Use stable deterministic identities and ordering.
- Avoid reflection fallbacks that hide missing metadata in production AOT paths.
- Test generated output and runtime consumption.
- Document unsupported language constructs and conversions explicitly.

## Verification

A successful generator compile is not proof of Native AOT compatibility. Verify generator output, application publish, trimming warnings, runtime metadata registration, and representative query/mutation behavior in the actual deployment target.

## Where to look in source

- C#: `Foundgine.Providers/Aot`, `Foundgine.Providers/Foundgine.Providers.Aot.Generator`.
- Java: `src/java/foundgine-aot-generator` and the annotations/metadata contracts in `foundgine-providers`.
- Existing guide: [AOT](../AOT.md).
