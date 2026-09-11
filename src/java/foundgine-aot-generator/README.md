# Foundgine AOT Generator

`foundgine-aot-generator` is the Java compile-time metadata generator for Foundgine.

It mirrors the intent of the .NET AOT generator: application domain declarations are inspected during compilation and converted into deterministic Java source. Runtime reflection is not required for the generated metadata path.

## Generated contract

The processor emits:

- `GeneratedFoundgineMetadata.build()` → `MetadataRegistry`
- stable entity, field and column identities
- entity/field aliases and weights
- semantic dimensions
- primary-key metadata
- event/temporal metadata
- relationship metadata and stable relationship identities
- generated entity descriptors

Generation is deterministic and sorted by semantic identity rather than source declaration order.

## Usage

Add `foundgine-aot-generator` as an annotation processor and configure:

```xml
<arg>-Afoundgine.generated.package=com.example.generated</arg>
<arg>-Afoundgine.generated.name=GeneratedFoundgineMetadata</arg>
```

The generated class is normal Java source and can be referenced by the application without a reflection dependency.
