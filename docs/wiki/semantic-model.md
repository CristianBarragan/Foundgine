# Semantic model, metadata and mapping

## Purpose

The semantic model is the application-owned description of what its data and operations mean. It protects callers from physical schema details and gives Foundgine a stable vocabulary for resolving and planning requests.

A physical schema may contain `crm_customer`, `cust_nm`, or several tables joined through technical keys. The semantic model can expose a `Customer` entity with a `name` field and explicit relationships. This is a mapping and meaning layer, not merely a rename table.

## Three layers

1. **Physical metadata:** provider-specific facts such as entity/table names, columns, keys, data types, and relationships.
2. **Semantic configuration:** business identity, field meaning, aliases, relationship cardinality, allowed operations, and model-level rules.
3. **Compiled semantic contract:** normalized, validated snapshots used by retrieval, planning, authorization, and execution.

Do not put application-specific generated code into the core contract layer. Generated metadata belongs at the integration boundary and should feed the same semantic contracts used by manually configured models.

## Main concepts and components

- **Model:** a named boundary containing semantic entities and their connections.
- **Entity:** a business concept addressable by callers, mapped to one or more physical sources.
- **Field:** a typed semantic value, optionally mapped to a physical column and equipped with semantic metadata.
- **Relationship:** an explicit connection between entities, including cardinality and join identity.
- **Connection configuration:** describes how a semantic model reaches a physical provider.
- **Persistence configuration:** describes physical storage mapping without making it the public semantic vocabulary.
- **Alias:** an alternate phrase for an entity or field. Aliases help retrieval but do not confer authority.
- **Stable identity:** IDs for models, entities, fields, relationships, and columns prevent accidental dependence on display names.
- **Fingerprint/attestation:** a deterministic representation that can identify the exact semantic contract used for a plan or decision.

## Configuration and compilation lifecycle

1. Define the model and entities.
2. Map semantic entities and fields to physical metadata.
3. Define relationships and cardinality explicitly.
4. Add descriptions, aliases, types, and operation constraints.
5. Compile and validate the model.
6. Freeze or attest the contract where the application requires immutability and reproducibility.
7. Publish the compiled contract to runtime consumers.

A frozen contract should not be mutated behind the runtime's back. Changes should produce a new, identifiable contract version/fingerprint so decisions can be traced to the semantics that were active at the time.

## Relationships and joins

Relationships are not arbitrary join hints. They define a permitted semantic path between entities. Cardinality matters: one-to-one, one-to-many, and many-to-many paths have different result and aggregate semantics. The physical join key should be centralized in mapping metadata; callers should not be allowed to invent join columns.

## Types, nullability and aggregates

Types and null semantics affect filtering, ordering, projection, and aggregate rewrites. An optimization is valid only when it preserves observable behavior, including empty and null cases. Provider capability must be considered before lowering an aggregate into provider-specific SQL.

## Where to look in source

- C#: `src/csharp/Foundgine.Core/Semantic/Configuration`, `SemanticEntityBuilder.cs`, `SemanticGraph*.cs`, `SemanticField.cs`, `SemanticRelationship.cs`, `SemanticModelFingerprint.cs`.
- Java: `src/java/foundgine-core/src/main/java/com/foundgine/core/semantic` and its configuration/model subpackages.
- Reading the model from code: [Fluent semantic reads (`SemanticQuery`)](semantic-query-builder), the same builder in C# and Java.
- Existing deeper guides: [Metadata to semantics](../METADATA-TO-SEMANTICS), [Mapping](../MAPPING), [Public API](../PUBLIC-API).

## Design rules

- Keep semantic names stable even when tables or vendors change.
- Keep physical mappings out of caller-controlled input.
- Keep authorization separate from descriptions and aliases.
- Validate cardinality and type semantics before execution.
- Make compiled contract identity deterministic and observable.
