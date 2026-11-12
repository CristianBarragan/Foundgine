# Foundgine AOT Generator (packaged in `foundgine-providers`)

The compile-time metadata generator for Foundgine ships inside the `foundgine-providers` artifact (there is no separate generator artifact).

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

Put `foundgine-providers` on the annotation processor path (`annotationProcessorPaths`) and configure:

```xml
<arg>-Afoundgine.generated.package=com.example.generated</arg>
<arg>-Afoundgine.generated.name=GeneratedFoundgineMetadata</arg>
```

The generated class is normal Java source and can be referenced by the application without a reflection dependency.

## JPA ORM access models

Annotate persistence classes separately from semantic metadata classes:

```java
@FoundgineOrmEntity
@Entity
public class CustomerEntity {
    public CustomerEntity() {}
    public Long getId() { return id; }
    public void setId(Long id) { this.id = id; }
}
```

The processor emits `GeneratedJpaModels.create()` with direct constructor/getter/setter
references. Supply that list to the four-argument `JpaEntities` constructor to enable strict
AOT mode. That constructor rejects missing generated mappings and does not use reflective
fallbacks. Only public entity classes with public no-argument constructors are emitted; other
construction/access patterns need an explicit `JpaEntityModel`.
