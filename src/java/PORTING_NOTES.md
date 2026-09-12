# Foundgine — Java: notes & progress

## Build tool: Maven

Chosen over Gradle because the source repo is one C# solution (`Foundgine.sln`)
containing ~50 projects with fairly conventional, static dependency graphs and
NuGet package references — that maps very directly onto a Maven **reactor**
(parent POM + modules), and Maven's declarative POM is the closest analogue
to `.csproj`. Gradle would be a reasonable choice too; nothing here is
Maven-specific, so it can be converted later if you'd rather have Gradle.

## Module layout (mirrors the .NET solution 1:1)

```
.NET project                    Java/Maven module            Status
--------------------------------------------------------------------------
src/Foundgine.Core            → foundgine-core               IN PROGRESS
src/Foundgine.Extensions      → foundgine-extensions          not started
src/Foundgine.Providers       → foundgine-providers           not started
src/Foundgine.Runtime         → foundgine-runtime             not started
src/csharp/tests/Foundgine.*.Tests       → */src/test/java               not started (6 test classes ported so far)
src/csharp/samples/Foundgine.SupplyChain*→ foundgine-samples-*           not started
src/csharp/benchmarks/*                  → foundgine-benchmarks-*        not started
src/csharp/security/Foundgine.RedTeam    → foundgine-redteam             not started
```

Within `foundgine-core`, C# namespaces map to Java packages 1:1:
`Foundgine.Core.Abstractions` → `com.foundgine.core.abstractions`,
`Foundgine.Core.Execution` → `com.foundgine.core.execution`,
`Foundgine.Core.Execution.Mutation` → `com.foundgine.core.execution.mutation`,
`Foundgine.Core.Semantic` → `com.foundgine.core.semantic`, etc.

## Progress: `foundgine-core`

### `Foundgine.Core.Abstractions` — DONE

13 C# files → 16 Java files. See the table from the previous pass (unchanged
this session): `EntityId`/`FieldId`/`ColumnId`/`ConnectionId`/`ModelId`/
`RelationshipId`/`AuthorizationId` (7 records + nested Jackson
(de)serializers), `SemanticIdentity`, `AuthorizationAccess`/
`AuthorizationOperation`/`AuthorizationDecision`, `AuthorizationPredicateKind`/
`AuthorizationPredicate`, `AuthorizationOperationName`, `MutationSchema`/
`MutationEntitySchema`/`MutationRelationshipSchema`, `FoundgineAbstractionsMarker`,
plus `SemanticId` and `UnsignedLongJson` (added for Java ergonomics).

### `Foundgine.Core.Execution` — PARTIAL (this session's work)

The C# `Execution/` folder has 34 files (18 top-level + 3 under `Security/`
+ 13 under `Mutation/`), not the 18 estimated in the previous pass — that
estimate undercounted the `Mutation/` subfolder. Of those 34, **17 are now
ported**; the other 17 are deferred (see below) because they transitively
depend on the `Semantic/` namespace, which has not been touched yet.

**Ported — `com.foundgine.core.execution` (self-contained: `Abstractions` and/or BCL only):**

| C# | Java |
|---|---|
| `ExecutionContext.cs` | `ExecutionContext.java` |
| `ExecutionContextKeys.cs` | `ExecutionContextKeys.java` |
| `FoundgineExecutionMarker.cs` | `FoundgineExecutionMarker.java` |
| `ExecutionResult.cs` (4 types) | `ExecutionResult.java`, `ExecutionPageInfo.java`, `ExecutionRow.java`, `ExecutionCellKey.java` (split one-file-per-type, unlike the C# original, to match this port's existing one-public-type-per-file convention) |
| `ExecutionEvidence.cs` (2 types) | `ExecutionEvidence.java`, `ExecutionEvidenceFactory.java` |
| `ExecutionReceipt.cs` (2 types) | `ExecutionReceipt.java`, `ExecutionReceiptFactory.java` |
| `SecurityInvariantAttestation.cs` | `SecurityInvariantAttestation.java` — its C# `using Foundgine.Core.Semantic.Security;` turns out to be unused dead code (the type references no `Semantic.Security` member), so it was portable now despite living in the same folder as the deferred security files |
| *(none — placeholder for .NET's `System.Threading` primitives)* | `CancellationToken.java`, `CancellationTokenSource.java` — see caveat below |

**Ported — `com.foundgine.core.execution.mutation` (self-contained):**

| C# | Java |
|---|---|
| `Mutation/IMutationExecutionProvider.cs` | `IMutationExecutionProvider.java` |
| `Mutation/MutationBatchResult.cs` | `MutationBatchResult.java` |
| `Mutation/MutationResult.cs` | `MutationResult.java` |
| `Mutation/MutationMaterializedResult.cs` (2 types) | `MutationMaterializedResult.java`, `MutationMaterializedNode.java` (split, same reasoning as `ExecutionResult` above) |
| `Mutation/MutationPlan.cs` | `ProviderMutationPlan.java` — ported as an `abstract class`, not a record: Java records cannot be extended (they're implicitly final), so a zero-component "opaque marker" abstract record has no faithful record equivalent. Subtypes are free to be records and get their own structural equality from their own fields. |
| `Mutation/ProviderMutationBatchPlan.cs` | `ProviderMutationBatchPlan.java` — same reasoning, `abstract class` |

**Ported — tests (`foundgine-core/src/test/java/...`):**
`ExecutionContextTest`, `SecurityInvariantAttestationTest` (both in
`com.foundgine.core.execution`), `MutationTest` (in
`com.foundgine.core.execution.mutation`).

**Deferred — depend on the not-yet-ported `Semantic/` namespace.** Porting
these now would mean guessing at APIs (`SemanticPlan`, `SemanticModel`,
`SemanticContractSnapshot`, `SemanticAuthorizationEvidence`,
`SemanticPlanAuthorizationBinding`, `SecurityInvariantIds`,
`SecurityInvariantRegistry`, `AggregateExecutionStrategy`,
`ExecutionOperation`, `SemanticQueryOptions`, `IMutationSchema`
implementations, `NestedMutationIntent`, etc.) that haven't been seen yet.
Revisit once `Semantic/` (or at least `Semantic.Planning`, `Semantic.Security`,
`Semantic.Authorization`, `Semantic.Results`) is ported:

- `com.foundgine.core.execution`: `ExecutionIR.cs`, `ExecutionIRBoundary.cs`,
  `ProviderPlan.cs`, `IProviderPlanCompiler.cs`, `IProviderPlanCache.cs`,
  `MemoryProviderPlanCache.cs`, `ProviderPlanCacheExtensions.cs`,
  `IExecutionProvider.cs`, `MaterializedResult.cs` (already flagged in the
  previous pass), `SecurityInvariantProof.cs`, `SecurityInvariantExecutionGate.cs`,
  `ExecutionAuthorizationRevalidation.cs`, `ResultMaterializer.cs`
- `com.foundgine.core.execution.security`: `Security/ProviderSecurityConformance.cs`,
  `Security/ProviderSecurityConformanceContract.cs`
- `com.foundgine.core.execution.mutation`: `Mutation/ExecutionMutationIR.cs`,
  `Mutation/IMutationBatchExecutionProvider.cs` (needs `ExecutionMutationIR`),
  `Mutation/MutationDependencyGraph.cs`, `Mutation/MutationDependencyLevels.cs`,
  `Mutation/MutationExecutionLevels.cs`, `Mutation/MutationResultMaterializer.cs`,
  `Mutation/MutationSecurityConformance.cs`,
  `Mutation/SemanticMutationExecutionLowerer.cs` (note: this one file actually
  lives in the C# `Foundgine.Core.Semantic.Planning.Mutation` namespace despite
  its path being under `Execution/Mutation/` — the eventual Java package for
  it is `com.foundgine.core.semantic.planning.mutation`, not
  `com.foundgine.core.execution.mutation`)

**Not yet ported at all:** `Semantic/` (~190 files across Aggregates,
Authorization, Capabilities, Expressions, IR, Intent, Metadata, Mutation,
Planning, Query, Resolution, Results, Security), `Serialization/` (3 files).
`Semantic/Planning` alone is ~384 KB of C#, larger than everything ported so
far combined.

## Key porting decisions

(Carried over from the previous pass, plus two new ones below.)

- **`ulong` → `long` (bit-pattern reinterpreted as unsigned).** See
  `UnsignedLongJson`; FNV-1a hash cross-checked against a reference Python
  implementation.
- **`readonly record struct` / `sealed record` → Java `record`.**
- **`[JsonConverter(typeof(X))]` → Jackson `@JsonSerialize`/`@JsonDeserialize`**
  with nested `StdSerializer`/`StdDeserializer`/`KeySerializer`/`KeyDeserializer`.
- **`IReadOnlySet<T>` / `IReadOnlyDictionary<K,V>` → `Set<T>` / `Map<K,V>`.**
  No immutability enforcement added yet.
- **Nullable value types → plain nullable object references.**
- **JSON library**: Jackson (`jackson-databind`).
- **New this session — C# default/optional positional-record parameters
  have no Java equivalent.** (`ExecutionResult`'s `PageInfo`/`Evidence`/
  `Receipt` all default to `null`; `ExecutionEvidence` similarly.) Ported as
  the full canonical constructor plus *one* convenience overload for the
  "just the required field(s)" case; any other partial combination must use
  the canonical constructor directly with explicit `null`s. Flagging this as
  a recurring pattern to watch for as more of `Semantic/` (which uses this
  C# feature heavily) gets ported.
- **New this session — `System.Threading.CancellationToken`/
  `CancellationTokenSource` have no built-in Java equivalent.** Ported as
  placeholder classes (`CancellationToken`, `CancellationTokenSource` in
  `com.foundgine.core.execution`) covering only the poll-based surface
  (`throwIfCancellationRequested`, `cancel`, `cancelAfter`,
  `createLinkedTokenSource`) actually exercised by ported code so far.
  **Known gap:** C#'s `CancellationToken.Register` (live callback
  propagation) is not ported, so a linked token source does not observe a
  cancellation requested on its parent token *after* the link was created —
  only a parent already cancelled at link time is honored. Revisit if/when a
  ported call site needs live propagation (e.g. via a simple listener list on
  `CancellationToken`).

## AOT

(Unchanged from the previous pass — not yet relevant.) The C# solution uses
.NET source generators and targets Native AOT. Closest Java equivalents:
GraalVM Native Image (deployment) and annotation processors
(`javax.annotation.processing`, source-generator role). Neither set up yet.

## Verification performed in this sandbox

Unlike the previous pass, **this sandbox now has a full JDK** (`openjdk-21-jdk-headless`,
installed via `apt-get` — `archive.ubuntu.com`/`security.ubuntu.com` are
allowlisted here even though Maven Central is not), so `javac` is available.
What was verified:

1. **Compiled every file in `foundgine-core/src/main/java` and
   `foundgine-core/src/test/java`** (the abstractions module from the
   previous pass, plus this session's execution/mutation additions) against
   hand-written stubs of the exact Jackson and JUnit 5 API surface actually
   used — `jackson-core`/`jackson-databind` (`JsonGenerator`, `JsonParser`,
   `JsonToken`, `ObjectCodec`, `JsonNode`, `ObjectMapper`,
   `TypeFactory`, `StdSerializer`/`StdDeserializer`, `KeySerializer`/
   `KeyDeserializer`) and `junit-jupiter`/`junit-jupiter-params`
   (`@Test`, `Assertions`, `@ParameterizedTest`, `@ValueSource`). Zero errors.
2. **Ran a standalone runtime smoke test** (not part of the Maven module;
   lived only in the sandbox) exercising actual logic rather than just
   syntax: `ExecutionContext.tryGetValue`/`ensureWithinDeadline`/
   `withDeadline`/`createDeadlineCancellationSource` (including the
   already-past-deadline failure path), `SecurityInvariantAttestation.create`'s
   missing-invariant sorting, `ExecutionEvidenceFactory.hash` determinism and
   length, `MutationBatchResult.totalAffectedRows`,
   `MutationMaterializedNode` relationship-grouping, and
   `ExecutionReceiptFactory.fingerprintResult` determinism. All passed.
3. Maven Central still isn't reachable from this sandbox's network allowlist,
   so `mvn compile` / `mvn test` against the *real* Jackson/JUnit jars still
   needs to run on a machine with normal internet access (or an internal
   Maven mirror) — the `javac`-against-stubs check is strong evidence of
   correctness but isn't a substitute for that.
4. The FNV-1a hash cross-check from the previous pass was not re-run (no
   FNV-1a code changed this session).

Run `mvn -pl foundgine-core -am test` from a normal environment to execute
all ported JUnit test classes against the real libraries.

## Progress: `Foundgine.Core.Semantic.Security` (this session)

The C# `Semantic/Security/` folder has 21 files: 4 top-level, 4 under
`Execution/`, and 13 under `Warrants/` (note: `Semantic.Security` turned out
to be less self-contained than the previous pass's "unblocks X" note
assumed — most of it depends on `Semantic.Capabilities`, which has not been
touched yet, and `Security/Execution/` and `Security/Warrants/` are mutually
dependent on each other). Of the 21, **7 types are now ported** (from 2
top-level files); the rest are deferred.

**Ported — `com.foundgine.core.semantic.security` (self-contained: BCL-only, no
`Capabilities`/`Warrants`/`Query` dependency):**

| C# | Java |
|---|---|
| `SecurityInvariant.cs` (3 types: the record + `SecurityInvariantPhase` enum + `SecurityInvariantIds` constants) | `SecurityInvariant.java`, `SecurityInvariantPhase.java`, `SecurityInvariantIds.java` (split, per this port's one-public-type-per-file convention) |
| `SecurityInvariant.cs` (`SecurityInvariantRegistry` static class) | `SecurityInvariantRegistry.java` — `IReadOnlyDictionary` → `LinkedHashMap` wrapped `Collections.unmodifiableMap`; `KeyNotFoundException` → `NoSuchElementException` (`InvalidOperationException` elsewhere in this port maps to `IllegalStateException`, but C#'s dictionary-miss exception is unchecked-lookup-specific, so `NoSuchElementException` is the closer BCL analogue) |
| `SecurityInvariant.cs` (`SecurityInvariantSet` record) | `SecurityInvariantSet.java` — `Require` → `require`, throws `IllegalStateException` per this port's existing `InvalidOperationException` mapping |
| `SecurityCapability.cs` (3 tiny records: `SecurityCapability`, `SecurityConstraint`, `SecurityInvariantRequirement`) | `SecurityCapability.java`, `SecurityConstraint.java`, `SecurityInvariantRequirement.java` (split) |

**Deferred — depend on the not-yet-ported `Semantic.Capabilities` and/or
`Semantic.Security.Warrants`/`Semantic.Security.Execution` (which are
themselves mutually dependent) and/or `Semantic.Query`:**

- `com.foundgine.core.semantic.security`: `SecurityCapabilityComposition.cs`
  (needs `SemanticCapability` from `Capabilities`, and `SecurityWarrant`/
  `SecurityWarrantAuthorization` from `Warrants`), `SecurityInvariantContractValidator.cs`
  (needs `SemanticCapability`, `SemanticCapabilityContract`),
  `SemanticCapabilitySecurityDefaults.cs` (needs `SemanticCapability`)
- `com.foundgine.core.semantic.security.execution`: `SecurityExecutionContext.cs`
  (needs `SecurityWarrant`), `SecurityResourceLimits.cs` (needs `Semantic.Query`),
  `ISecurityExecutionContextProvider.cs`,
  `DelegateSecurityExecutionContextProvider.cs`,
  `SecurityExecutionContextProviderExtensions.cs` (all three only reference
  `SecurityExecutionContext` itself, but that's enough to block them until
  it's portable)
- `com.foundgine.core.semantic.security.warrants`: all 13 files
  (`SecurityWarrant.cs`, `SecurityWarrantAuthorization.cs`,
  `SecurityWarrantCanonicalizer.cs`, `SecurityWarrantCrypto.cs`,
  `SecurityWarrantDelegationChain.cs`, `SecurityWarrantDelegationCompromise.cs`,
  `SecurityWarrantDelegationConcurrency.cs`,
  `SecurityWarrantDelegationStateMachine.cs`, `SecurityWarrantDelegationTrust.cs`,
  `SecurityWarrantExecutionTrust.cs`, `SecurityWarrantRevocation.cs`,
  `SecurityWarrantReplay.cs`, `FileSecurityWarrantReplayStore.cs`,
  `SecurityWarrantTrustTransition.cs`) — not yet read in detail; worth its own
  pass since warrant delegation/crypto/replay is a substantial, security-critical
  subsystem in its own right and shouldn't be rushed through as a side effect of
  chasing `Semantic.Capabilities` dependencies.

Verified this session's 7 new types compile cleanly standalone
(`javac -d out $(find src/main/java/com/foundgine/core/semantic -name '*.java')`
— zero errors, no external dependency needed since none of them touch Jackson).
Did not re-verify the rest of the module this session (no persisted Jackson/JUnit
stub jars from the previous pass's sandbox — the filesystem resets between
sessions — and Maven Central is still unreachable here); the previous pass's
`abstractions`/`execution`/`execution.mutation` code is unchanged and was
already verified in that pass.

## Next step

Two independent directions are now unblocked and either is reasonable to pick
up next:

1. **`Semantic.Capabilities`** — unblocks the 3 deferred top-level
   `Semantic.Security` files above (`SecurityCapabilityComposition`,
   `SecurityInvariantContractValidator`, `SemanticCapabilitySecurityDefaults`),
   which in turn are prerequisites for the `Execution/` deferred-list from the
   previous session's `Semantic.Security` guess (that guess was wrong — see
   above — the real unblock path runs through `Capabilities`, not the other
   way around).
2. **`Semantic.Security.Warrants`** (13 files) — self-contained from
   `Capabilities`'s point of view (nothing above references it needing
   `Capabilities`), but its own internal fan-in/fan-out across the 13 files
   hasn't been mapped yet. Do that mapping first before assuming any single
   file in it is portable in isolation.

`Semantic.Planning` (~384 KB, the long pole flagged in the previous pass)
still hasn't been started and still depends on unknown amounts of whichever
of the above gets built first.

## Progress: `Foundgine.Core.Semantic.Capabilities` (this session)

The C# `Semantic/Capabilities/` folder is a single file,
`SemanticCapabilityContract.cs` (346 lines, 6 types). Of those 6, **5 are
ported**; the 6th (`SemanticCapabilityContractDiscovery`) is deferred.

**Ported — `com.foundgine.core.semantic.capabilities`:**

| C# | Java |
|---|---|
| `SemanticCapabilityContract` record | `SemanticCapabilityContract.java` |
| `SemanticCapability` record (9 positional ctor params + 5 `init`-only properties with defaults) | `SemanticCapability.java` — all 14 values promoted to record components (Java records have one canonical constructor); `ofDefaults(...)` is the 9-arg convenience overload matching C# call sites that rely on the `init` defaults (`Operation="read"`, `HasSideEffects=false`, `IsIdempotent=false`, `Version=SemanticVersionSet.CurrentCapabilityVersion`, `RequiredSecurityInvariants=[]`); the computed property `EffectiveSecurityInvariants` is ported as an instance method |
| `SemanticCapabilityInput` record (`Description = null` default) | `SemanticCapabilityInput.java` — canonical 4-arg constructor plus a 3-arg convenience overload, per this port's established default-param pattern |
| `SemanticCapabilityConstraint` record | `SemanticCapabilityConstraint.java` |
| `SemanticCapabilityEffect` record | `SemanticCapabilityEffect.java` |

**Deferred:**

- `SemanticCapabilityContractDiscovery` (the static builder that walks a
  `SemanticModel` + `ISemanticAuthorizationPolicy` to produce a
  `SemanticCapabilityContract`) — needs `SemanticModel`, `SemanticTraversal`,
  `SemanticAuthorizationCapability`, `SemanticFieldAuthorizationCapability`,
  `SemanticAuthorizationCapabilityDiscovery`, `ISemanticAuthorizationPolicy`,
  none of which are ported. This is effectively gated on `Semantic.Metadata`/
  the core model type and `Semantic.Authorization`.

**Related — `com.foundgine.core.semantic`:**

`SemanticVersion.cs` → `SemanticVersionSet.java`, **partial port**. Only the
record shape and the four `Current*Version` constants are ported (all that
`SemanticCapability`'s default `Version` needs); the static factory
`SemanticVersionSet.For(SemanticModel)` is deferred pending `SemanticModel`.

## Progress: `Foundgine.Core.Semantic.Security` (continued, this session)

With `SemanticCapability` now portable, its two dependents from the
previous session's deferred list are unblocked:

| C# | Java |
|---|---|
| `SemanticCapabilitySecurityDefaults.cs` | `SemanticCapabilitySecurityDefaults.java` — the C# method name `For` is renamed `forCapability` because `for` is a reserved word in Java |
| `SecurityInvariantContractValidator.cs` | `SecurityInvariantContractValidator.java` |

**Still deferred** (need `Semantic.Security.Warrants`, not yet touched):
`SecurityCapabilityComposition.cs` (needs `SecurityWarrant`,
`SecurityWarrantAuthorization`). The `Security/Execution/` (4 files) and
`Security/Warrants/` (13 files) subfolders are unchanged from the previous
session's notes — still not started.

## Verification performed this session

Maven Central is still unreachable from this sandbox. Rather than re-derive
one-off stubs by hand for each compile check as the previous two sessions
did ad hoc, this session built a small reusable stub source tree (kept in
the sandbox only, **not** part of the Maven module or this zip) covering the
actual Jackson/JUnit surface this codebase uses:
`com.fasterxml.jackson.core` (`JsonGenerator`, `JsonParser`, `JsonToken`,
`ObjectCodec`), `com.fasterxml.jackson.databind` (`JsonNode`, `BeanProperty`,
`JavaType`, `DeserializationContext`, `SerializerProvider`,
`KeyDeserializer`, `ObjectMapper`), `com.fasterxml.jackson.databind.type`
(`TypeFactory`), `com.fasterxml.jackson.databind.annotation`
(`JsonSerialize`, `JsonDeserialize`), `com.fasterxml.jackson.databind.ser.std`/
`deser.std` (`StdSerializer`, `StdDeserializer`), and
`org.junit.jupiter.api`/`params` (`Test`, `Assertions`, `ParameterizedTest`,
`ValueSource`).

Compiled **all of `foundgine-core/src/main/java` and
`foundgine-core/src/test/java`** together (not just the new files in
isolation) against these stubs with `javac`: zero errors. This is a stronger
check than previous sessions' file-by-file/package-by-package compiles,
since it confirms the new `semantic`/`semantic.capabilities`/`semantic.security`
code links correctly against the existing `abstractions`/`execution`/
`execution.mutation` code (e.g. `SemanticCapability` → `EntityId`,
`AuthorizationDecision` from `abstractions`; `SemanticCapabilitySecurityDefaults`
→ `AuthorizationAccess`). Still not a substitute for `mvn -pl foundgine-core
-am test` against the real Jackson/JUnit jars on a machine with normal
internet access.

## Next step (updated)

Two independent directions remain, per the previous session's assessment
(now narrowed since `Capabilities` is done):

1. **`Semantic.Security.Warrants`** (13 files) — unblocks
   `SecurityCapabilityComposition` and the `Security/Execution/` subfolder
   (`SecurityExecutionContext` and its 3 dependents). Internal fan-in/fan-out
   across the 13 files still hasn't been mapped — do that before assuming
   any single file is portable in isolation.
2. **`Semantic.Metadata`/the core model type (`SemanticModel`) and
   `Semantic.Authorization`** — unblocks `SemanticCapabilityContractDiscovery`
   (the one remaining deferred piece of `Capabilities`) and
   `SemanticVersionSet.For`.

`Semantic.Planning` (~384 KB) is still the long pole and still untouched.

## Continued pass — Semantic.Security.Warrants + Security.Execution

The next tranche completed the security warrant subsystem and the host-owned
security execution context boundary.

### Added

- `com.foundgine.core.semantic.security.warrants` — 39 Java source/test-support
  files from the C# warrant/delegation/replay/revocation surface, including:
  `SecurityWarrant`, `CapabilityGrant`, canonicalization/digesting, signing and
  verification, attenuation, delegation-chain validation, revocation/replay,
  delegation concurrency/compromise/trust state, and the in-memory/file-backed
  stores.
- `com.foundgine.core.semantic.security.execution.SecurityExecutionContext`
- `ISecurityExecutionContextProvider`
- `DelegateSecurityExecutionContextProvider`
- `SecurityExecutionContextProviderExtensions`
- `SecurityExecutionContextTest`

The execution-context boundary intentionally fails closed when the host has
not established a trusted context. The cache partition includes the complete
warrant digest and escapes `\\` and `|` so attacker-controlled context fields
cannot alias another partition.

### Security correctness fix found during verification

The first smoke test exposed a Java-port bug in `SecurityWarrantConstraints`:
the no-argument constructor's `this(null, ...)` call resolved to the generated
record constructor rather than the normalization overload, leaving its list
components `null`. That could reach `SecurityWarrantCanonicalizer` and cause a
`NullPointerException` while calculating the authority cache partition/digest.

The constructor now explicitly selects the normalization overload so default
constraints produce empty immutable lists, matching the intended C# semantics.
This was caught by executing the new security-context smoke path rather than
only compiling the sources.

### Verification

Compiled the complete warrant package plus the new security execution package
with `javac` on JDK 21: zero compilation errors. Ran a standalone security
execution smoke test covering:

- warrant digest generation through the cache-partition path;
- delimiter/backslash escaping in the authority partition;
- host-context propagation through the delegate provider; and
- fail-closed behavior when no trusted context exists.

Result: `security execution smoke: PASS`.

`SecurityResourceLimits` / `SecurityResourceLimitValidator` remains deferred
until the Java `Semantic.Query` request/filter/selection types they consume are
ported; no placeholder API was introduced merely to force compilation.

## Next porting priority

Continue with the remaining provider-independent semantic core, prioritizing
`Semantic.Metadata` / `SemanticModel` and `Semantic.Authorization`, then return
to the deferred `SecurityCapabilityComposition`, `Security.Execution` resource
limits, and capability-contract discovery. Keep `Semantic.Planning` behind
those contracts rather than inventing Java APIs that diverge from the C#
authoritative reference.

## Progress: `Foundgine.Core.Semantic.Metadata` (continuation 2026-09-11)

Ported the provider-neutral metadata primitives that do not require the still-unported semantic graph or mutation schema:
`StorageEntityId`, `ColumnReference`, `ColumnMetadata`, `AliasDeclaration`, `FieldMetadata`, `RelationshipMetadata`, `ConnectionFieldMetadata`, `ConnectionMetadata`, `ConversionMetadata`, `EntityMetadata`, `ModelMetadata`, `AuthorizationMetadata`, `IMetadataProvider`, `IMetadataCatalog`, `FoundgineMetadataMarker`, and `MetadataRegistry`.

`SemanticModelDiscovery` and the `IMutationSchema` portion of the C# registry remain deferred until the corresponding semantic model/mutation contracts are ported; no placeholder API was invented for those dependencies.


## Continuation — 2026-09-11 (planning/security completion cleanup)

Inspected the current Java tree against the C# semantic source rather than relying
only on the older deferred list. The repository already contains a substantial
`com.foundgine.core.semantic.planning` port (all 49 C# `Semantic/Planning` source
types are represented, with a few Java-only split/helper types). The older
`com.foundgine.core.planning` directory was an unused duplicate package and was
removed to prevent duplicate-class compilation failures; all repository Java
references use `com.foundgine.core.semantic.planning`.

Completed the remaining small provider-independent gaps found by comparing the
semantic source trees:

- `SemanticVersionSet.forModel(SemanticModel)` now derives the model version as
  `sha256:<ContractFingerprint>`, matching the C# `For(SemanticModel)` behavior.
- Added declarative semantic annotations:
  `SemanticEntityAttribute`, `SemanticPolicyAttribute`, and
  `SemanticFieldAttribute`, plus the `FoundgineSemanticAttributes` marker.
- Added the missing security-warrant delegation-chain validator and deterministic
  chain digest in `SecurityWarrantDelegationChainValidator`.
- Added single-use warrant replay protection:
  `ISecurityWarrantReplayStore`, `MemorySecurityWarrantReplayStore`, and
  `SecurityWarrantReplayGuard`.
- Added delegation optimistic-concurrency contracts and in-memory store:
  `SecurityWarrantDelegationConcurrencySnapshot`,
  `SecurityWarrantDelegationReservation`,
  `ISecurityWarrantDelegationConcurrencyStore`,
  `MemorySecurityWarrantDelegationConcurrencyStore`, and
  `SecurityWarrantDelegationConcurrencyGuard`.

The delegation implementation preserves the C# security boundaries: exact
parent id/digest binding, issuer/subject continuity, depth/path validation,
ancestor-splice detection, duplicate id/digest rejection, parent sequence
optimistic concurrency, child identity/nonce uniqueness, and attenuation before
reservation.

Verification note: Maven is not installed in this execution environment and the
sandbox cannot resolve Maven Central dependencies. A full `javac` invocation was
also intentionally not treated as a pass because this module's Jackson
dependencies are unavailable. The first full-source check did catch and remove
an actual duplicate-package problem (`com.foundgine.core.planning` vs
`com.foundgine.core.semantic.planning`). No placeholder provider API was added.


## Continuation — 2026-09-11 (Runtime API/routing foundation)

Started the application-facing Java runtime module after the provider-independent semantic core. Added Maven module `foundgine-runtime` and ported the provider-neutral runtime contracts:

- `IFoundgineExecutor` / `IFoundgine`
- mutation-facing request/result/approval records and `FoundgineMutationExtensions`
- `DryRunResult` and `PlanApproval`
- routing contracts: `IRoutingRule`, `IRoutingEngine`, `RoutingContext`, `DefaultRoutingEngine`, and `TaskContract`
- initial `RiskScore` value type used by routing

Java uses `CompletableFuture` for the asynchronous application boundary and `OffsetDateTime`/`Duration` for the corresponding .NET `DateTimeOffset`/`TimeSpan` values. The routing rule API uses an explicit `RouteDecision` rather than C#'s `out` parameter.

This is intentionally only the provider-neutral runtime foundation; the remaining runtime control-plane, execution engines, hosting/DI adapters, and provider modules will be ported after these contracts are established rather than introducing placeholder provider behavior.

## Continuation — 2026-09-11 (AOT identity + provider reactor cleanup)

Ported the provider-side stable identity helper as
`com.foundgine.providers.aot.generator.GeneratorSemanticIdentity`, preserving
Foundgine's namespace/key composition and UTF-8 FNV-1a 64-bit hashing contract.
The Java `long` deliberately preserves the full 64-bit bit pattern, including
values whose unsigned representation is above `Long.MAX_VALUE`.

Aligned the Java AOT annotations with the C# contract (`id`, `name`, storage /
column metadata, relationship and connection mapping, authorization, semantic
dimension and event metadata), and updated the reflection metadata fallback to
use the same stable semantic identity algorithm instead of Java
`String.hashCode()` (which is only 32-bit and would not be cross-language stable).

Added the providers module to the Maven reactor and corrected stale child POM
parent versions from `2.1.1-SNAPSHOT` to the current Java-port parent
`2.0.0-SNAPSHOT`.

Verification: the AOT identity/annotation path compiles with JDK 21; the stable
hash smoke check passes for canonical keys including `entity:Order` and
`field:Order.Id`. Full Maven verification remains blocked in this sandbox by
unavailable Maven/Jackson dependencies.

## Continuation — 2026-09-11 (deployment pipeline switch)

Added the Java deployment pipeline as an **opt-in parallel path** without making
Java the default release implementation before parity is reached.

The existing GitHub Actions build now exposes a `workflow_dispatch` deployment
selector:

- `none` — build/test only; publish nothing
- `dotnet` — publish the existing NuGet release path
- `java` — run the Java Maven pipeline and publish Java artifacts
- `both` — publish both artifact families

For normal tag releases, the persistent repository variable
`FOUNDGINE_DEPLOY_TARGET` controls the same switch (`dotnet`, `java`, `both`, or
anything else for no deployment). Java CI remains opt-in through
`FOUNDGINE_JAVA_PIPELINE=true` so an incomplete Java cannot accidentally
block or replace the production .NET release.

Java Maven POMs now use the Maven CI-friendly `${revision}` property, allowing a
release tag such as `v2.0.0` to produce Java artifacts at version `2.0.0` without
editing every module POM.

Java publishing uses the `foundgine` Maven server id with `MAVEN_USERNAME` /
`MAVEN_PASSWORD` repository secrets and an optional `MAVEN_REPOSITORY_URL`
repository variable. The default is GitHub Packages; Maven Central should only
be selected after the repository's Central publishing/signing configuration is
added.

**Important:** this is the deployment switch/scaffold only. Java is not yet at
C# parity for samples, benchmarks, security, and all runtime/provider surfaces,
so `FOUNDGINE_JAVA_PIPELINE` should remain disabled until those gates pass.

## Progress: `Foundgine.SupplyChain.Advanced` (current pass)

The Advanced SupplyChain sample is now started as a real Java/Maven module:
`src/java/samples/foundgine-supply-chain-advanced`.

Ported in this pass:

- deterministic Advanced SupplyChain domain model and fixture seed data;
- Supplier/Product/BOM/PurchaseOrder/Shipment/Inventory/CustomerOrder entities;
- semantic aliases (`Vendor`, `Seller`, `PO`, `POs`, `Buy`, `Buys`, `DueDate`);
- event metadata for inventory movement, production output and compliance incidents;
- tenant/warehouse authorization context;
- generic client-claim validation model with reserved identity-key spoofing detection;
- SupplyChain claim schema for scope, warehouse, max rows, reason, change ticket and bounded `not_after` evidence;
- fail-closed expiry/evidence retraction;
- recursive BOM traversal with explicit cycle detection;
- authorization-narrowed fulfillment calculation;
- initial Advanced sample executable and JUnit test coverage for the security fixtures.

The Java fixture deliberately mirrors the C# Advanced sample's adversarial data,
including the tenant-B warehouse and deliberate BOM cycle. The next Advanced
porting stage is the actual Foundgine semantic pipeline: manual semantic model,
claim-aware authorization policy, grounding/retrieval cases, high-assurance
`PlaceOrder` mutation, MCP surface, PostgreSQL provider wiring, and the Advanced
benchmark/security gates.

Sandbox verification for this pass: the Advanced sample main sources compile
against the Java AOT annotation surface and execute successfully. Maven itself
is not installed in this sandbox, so the full reactor test command could not be
run here.

## Continuation — 2026-09-11 (JDBC SQL mutation execution boundary)

Closed a concrete provider parity gap in the Java SQL mutation path.

- `SqlMutationExecutionProvider` now accepts an `IMetadataProvider` and can lower
  canonical `ExecutionMutationIR` directly through `SqlMutationCompiler` before
  execution.
- PostgreSQL `RETURNING` results are consumed from the JDBC result set instead
  of `getGeneratedKeys()`, matching the SQL emitted by the mutation compiler.
- JDBC execution now restores the connection's original `autoCommit` state
  instead of always forcing it back to `true`.
- Cancellation is checked before every physical mutation in a batch.
- Foundgine's SQL writer retains deterministic `@pN` parameter names for
  cross-language parity; the JDBC boundary now normalizes those placeholders to
  positional `?` placeholders before preparing the statement.
- Added focused provider tests for placeholder normalization, including a guard
  that ordinary `@` characters are not rewritten.

The PostgreSQL multi-operation CTE/unnest optimizer remains intentionally
conservative. It must not be treated as complete until the Java preserves
operation correlation, generated-value propagation, conflict-collapse detection,
and dependency-level ordering from the C# implementation. The executable Java
path therefore uses the sequential SQL compiler/batch provider until that
optimized lowering is ported completely.

## Continuation — 2026-09-11 (Advanced SupplyChain semantic contract)

Continued the Java Advanced SupplyChain port beyond the fixture/security shell and added the first real semantic-contract layer:

- Added `ManualSupplyChainSemanticModel`, mirroring the C# hand-authored overlay for `Product` and `ProductComponent` (aliases `Item`/`Item2`, `PartNumber`, SKU pattern, safety-stock and BOM quantity constraints, and typed relationships).
- Added `SupplyChainSemanticModel` with deterministic semantic identities for the complete Advanced domain and explicit application relationships for Supplier/Certification/Incident, Warehouse/Inventory, PurchaseOrder/Line/Shipment, Inventory/Product, CustomerOrder/Line, and the Product BOM graph.
- Added `SupplyChainAuthorization` on top of the provider-independent Core authorization API. Tenant and warehouse predicates are represented as semantic authorization predicates; sensitive Supplier risk and Inventory quarantine fields retain role-specific access; write and named-operation gates consume only validated claims.
- Added semantic-contract tests covering topology, aliases/constraints, warehouse predicate narrowing, sensitive-field authorization, and lexical alias commitment (`Item` → `Product`).
- Updated the Java Advanced sample executable to report the semantic entity count and contract fingerprint.

This pass deliberately keeps semantic truth in Core and SupplyChain-specific policy/data in the sample, matching the .NET architecture rather than implementing authorization directly in the Java fixture/scenario layer.

The next Advanced stage is the executable high-assurance path: `PlaceOrder` mutation intent/schema + authorization + inventory/price ownership checks + transaction/idempotency/evidence semantics, followed by MCP/HTTP wiring and the Advanced benchmark/security gates.

## Advanced SupplyChain — MCP boundary and pricing correctness

The Java Advanced sample now exposes a transport-neutral MCP facade through the existing provider MCP contracts. `AdvancedMcpFacade` maps `place_order` and `cancel_order` JSON tool calls into the Runtime mutation pipeline and exposes bounded capability discovery without leaking MCP concepts into the domain services.

The product fixture now separates `SafetyStock` from `UnitPrice`. `PlaceOrderService` resolves and records the server-side product unit price, so callers cannot supply or influence pricing through the mutation request. Product 4 is seeded at 34.00 and a two-unit order therefore totals 68.00.


## Advanced SupplyChain — PostgreSQL mutation path
The Java Advanced sample now has a real JDBC/PostgreSQL high-assurance mutation boundary. `PostgresSupplyChainStore` owns the physical transaction, PostgreSQL advisory idempotency lock, server-side product pricing, tenant/warehouse checks, `FOR UPDATE SKIP LOCKED` inventory selection, exact lot allocation, atomic order creation, cancellation and inventory restoration. `PostgresAdvancedMutationProvider` adapts that physical store behind the Runtime `IMutationBatchExecutionProvider` boundary. The sample includes `database/schema.sql`, deterministic `database/seed.sql`, and a PostgreSQL 16 Docker Compose fixture.

## Test parity — semantic unit tests

The Java is now actively mirroring the C# semantic unit-test surface rather than only testing Java-specific implementations. Added parity coverage for entity/field/relationship aliases, protocol-neutral query and relationship filters, and semantic mutation intent dependencies/conflict/filter semantics. Continue porting tests by subsystem until the Java suite tracks the C# unit and integration suites.

## Test parity — security authority and warrant boundaries

The Java test pass now mirrors the C# security contract around capability composition,
tenant-bounded authority, warrant replay/revocation, revocation snapshots, and execution
authorization revalidation. These tests intentionally validate the language-neutral
security invariant rather than reproducing C# transport/framework details.

## Test parity — execution/E2E boundary

Mirrored the C# E2E execution-boundary coverage into Java for semantic-plan lowering, canonical mutation execution IR, security invariant attestation, and deterministic execution evidence. These tests intentionally validate the provider-neutral execution boundary rather than transport-specific C# frameworks.

## Test parity — execution/result boundaries

The Java test suite now mirrors additional C# E2E contracts around result materialization, nested mutation result accounting, and connection traversal planning. These tests target semantic/provider-neutral behavior rather than transport-specific implementation details.


## Test parity — semantic identity and contract boundaries
The Java suite now mirrors additional C# semantic contract tests for stable identity namespaces, reserved zero identities, protocol-neutral query/relationship filters, and semantic contract fingerprint attestation. These tests use the Java Core APIs directly while preserving the C# behavioral contract.

## Continuation — 2026-09-12 (semantic aggregate + lexicon projection parity)

Continued the semantic parity pass from the remaining untouched C# test surface.

- Ported `SemanticAggregateSemanticsTests.cs` as
  `SemanticAggregateSemanticsParityTest`, covering COUNT/MIN/MAX empty-collection
  behavior, NULL-input behavior, duplicate sensitivity, catalog completeness,
  fail-closed lookup, `TryGet`/`For` equivalence, and cardinality-proof defaults.
- Hardened `SemanticAggregateSemanticsCatalog.tryGet(null)` to return an empty
  result rather than leaking the JDK `Map.of` null-key `NullPointerException`.
  `forAggregate(null)` now fails through the same explicit unsupported-aggregate
  boundary as other unregistered values.
- Ported `SemanticLexiconProjectionTests.cs` as
  `SemanticLexiconProjectionParityTest`, covering entity/node projection,
  field ownership and aliases, relationship source/target grounding, logical
  traversal endpoint names, and the bare-entity entity/node-only boundary.

Verification:
- Both new parity test sources syntax-compiled against the existing Java Core
  classes with lightweight JUnit API stubs.
- A direct JDK 21 smoke execution exercised the aggregate catalog and entity/node
  lexicon projection and passed.
- Full Maven/Surefire verification remains unavailable in this sandbox because
  Maven and external dependency artifacts are not installed.

## Continuation — 2026-09-12 (delegation state / chain parity)

Added the next security-warrant parity tranche:

- `SecurityWarrantDelegationStateMachineParityTest`
  - registration creates an ACTIVE snapshot
  - revoked warrants cannot delegate
  - compromised warrants cannot rotate keys back into authority
  - active key rotation advances the sequence without changing authority state
  - duplicate registration is rejected
- `SecurityWarrantDelegationChainParityTest`
  - single-root chain validation
  - expected-root digest binding
  - empty-chain rejection
  - root ancestry tampering rejection
  - deterministic chain digest

Verification:
- Both new test sources syntax-compile against the existing Core classes using lightweight JUnit API stubs.
- The complete `semantic.security.warrants` main-source subtree compiles cleanly with JDK `javac`.
- Maven/Surefire remains unavailable in this sandbox (`mvn` is not installed), so no claim of a full Maven test run is made.

## Continuation — 2026-09-12 (security penetration + provider conformance parity)

Ported the next security-boundary tranche from `Foundgine.Security.Tests`:

- `SemanticBoundaryPenetrationParityTest` covers denied root entities, field pruning before planning, descendant removal through denied relationships, authorization-predicate preservation, and plan-fingerprint isolation.
- `MutationAuthorizationPenetrationParityTest` covers unauthorized mutation fields, return fields, filters, and both orderings of a mixed authorized/unauthorized semantic mutation batch. Authorization failure is verified to return no partially authorized plan.
- `ProviderExecutableConformanceParityTest` covers concrete provider conformance evaluation, missing-invariant certification, exact provider-plan binding, exact Execution IR binding, provider identity mismatch, and hostile provider-plan evidence.
- `ProviderSecurityConformanceMatrixParityTest` covers the in-memory/SQL query profile, the high-assurance PostgreSQL transfer profile, generic SQL failing high-assurance mutation requirements, unknown-provider fail-closed behavior, and rejection of invented provider invariants.

Also fixed two concrete Java-port compilation gaps found while validating the Core source tree:

- `FieldMetadata.effectiveAliases()` now mirrors the C# null-as-empty alias behavior used by `SemanticModelDiscovery`.
- `StorageEntityId.KeyDeserializer` now explicitly extends Jackson's `com.fasterxml.jackson.databind.KeyDeserializer`; the nested class name previously shadowed the imported base type and caused a Java compilation cycle.

Verification in this sandbox:
- All `foundgine-core/src/main/java` sources syntax-compile with JDK 21 against lightweight Jackson API stubs.
- The four new parity test classes syntax-compile against the compiled Core sources and lightweight JUnit/Jackson stubs.
- Full Maven/Surefire execution remains unavailable because Maven and external dependency artifacts are not installed in this sandbox.

## Continuation — 2026-09-12 (in-memory provider execution parity)

Completed the first concrete provider-execution parity step for the Java.

Ported the C# in-memory provider surface into Java:

- `InMemoryRow` — entity-scoped backing row with `FieldId`-keyed values.
- `InMemoryDataSet` — deterministic entity-partitioned backing store.
- `InMemoryPlan` — provider-specific `ProviderPlan` carrying the exact `ExecutionIR`.
- `InMemoryCompiler` — provider-neutral execution, relationship traversal, root filtering,
  ordering, offset/limit pagination, authorization predicates, projection/cell shaping,
  and provider security-conformance evaluation.
- `InMemoryExecutionProvider` — `IExecutionProvider` integration.

The previous Java `InMemoryProvider` shell was removed in favor of the same five-part
provider surface used by the C# implementation.

Added behavioral parity coverage for provider-specific planning, projection boundaries,
backing-value leakage, filtering/order/pagination, relationship traversal, authorization,
security conformance, and execution refusal without authorization provenance.

Verification:
- All six in-memory Java production/test sources compile with JDK 21 against the existing
  Java Core/runtime classes and lightweight dependency stubs.
- A direct JDK 21 behavior smoke test exercised filtering, relationship traversal, projection
  boundaries, and provider security conformance successfully.
- Full Maven/Surefire execution remains unavailable because Maven and external dependency
  artifacts are not installed in this sandbox.

Next provider step: implement PostgreSQL batched mutation compilation/execution parity.

## Continuation — 2026-09-12 (PostgreSQL batched mutation parity)

Completed the next provider step by replacing the Java PostgreSQL batch compiler
placeholder with the concrete correlation-aware lowering used by the C# provider.

Implemented:

- `PostgresBatchedMutationCompiler.compile(ExecutionMutationIR)` and the legacy
  `MutationBatchPlan` entry point.
- dependency-level computation and cycle detection.
- physical grouping of same-level Create/Upsert operations by mutation shape.
- typed PostgreSQL array parameter generation for `unnest(...)` batch inputs.
- compiler-owned `__fg_corr` ordinals; user-visible keys are never used as a
  correlation surrogate for generated identities.
- cross-level reference propagation through ord-map CTEs.
- PostgreSQL 17 `MERGE ... RETURNING` for batched Create operations.
- ON CONFLICT handling, duplicate literal-key detection, and key-based recovery
  for batched Upsert operations.
- Update/Delete folding into the same statement where safely representable.
- safe `tryCompile(...)` fallback for unsupported/unsafe shapes.
- explicit row/group correlation metadata on `SqlBatchedMutationPlan`.

Also ported `PostgresBatchedMutationExecutionProvider`:

- executes the complete compiled batch as one JDBC statement;
- binds PostgreSQL array parameters through `Connection.createArrayOf(...)`;
- reconstructs logical `MutationResult` values from `__fg_corr` rather than
  assuming database result order;
- rejects unknown groups, invalid ordinals, missing correlation values, and
  missing results for ordinal-addressable operations;
- converts returned JSON values using the declared semantic field CLR/Java type;
- falls back to the sequential SQL mutation provider when batched compilation is
  unavailable;
- exposes concrete mutation security-conformance evidence for parameterization
  and atomic mutation execution.

Added `PostgresBatchedMutationCompilerParityTest` covering the Execution IR
entry point, explicit compiler-owned correlation, generated-identity correlation,
duplicate-key fallback, and cross-dependency-level correlation metadata.

Verification:
- Source inspection against `PostgresBatchedMutationCompiler.cs` and
  `PostgresBatchedMutationExecutionProvider.cs` was used as the behavioral reference.
- The changed Java sources were checked with JDK 21; full Maven/Surefire execution
  remains unavailable in this sandbox because Maven/dependency artifacts are not
  installed.

Next provider step: complete the remaining PostgreSQL provider parity, especially
real JDBC/PostgreSQL E2E execution, transaction/cancellation behavior, and any
remaining provider-specific mutation result/conformance cases before moving on to
Java AOT annotation processing.

## Pass 11 — PostgreSQL physical execution and cancellation parity

This pass closes the PostgreSQL execution boundary rather than adding another
compiler-only surface. `PostgresBatchedMutationExecutionProvider` now has explicit
transaction ownership semantics: provider-owned batches start, commit, and roll
back a JDBC transaction, while callers can pass `ownsTransaction=false` to
participate in an existing transaction without taking ownership of commit or
rollback. The sequential `SqlMutationExecutionProvider` fallback follows the same
ownership model.

JDBC command execution now registers cancellation callbacks and invokes
`PreparedStatement.cancel()` when a live `CancellationToken` is cancelled.
`CancellationToken` therefore supports live callback registration, and linked
`CancellationTokenSource` instances propagate cancellation after link creation.

A real PostgreSQL JUnit E2E suite was added for the batched provider. It is opt-in
via `FOUNDGINE_POSTGRES_CONNECTION_STRING` and verifies generated identity
propagation across dependency levels, caller-controlled transaction rollback,
provider-owned commit behavior, and fail-fast cancellation.

The PostgreSQL driver and JUnit dependencies were added explicitly to the Java
providers module so these tests are first-class Maven tests rather than custom
compile-only fixtures.

Verification:
- JDK 21 is available in the sandbox.
- Maven is not installed in the sandbox, so Maven/Surefire and live PostgreSQL
  execution cannot be run here.
- The new tests and production changes were structurally checked against the
  corresponding C# provider transaction/cancellation contract.

Next provider step: complete the remaining PostgreSQL provider parity, especially
real JDBC/PostgreSQL E2E execution, transaction/cancellation behavior, and any
remaining provider-specific mutation result/conformance cases before moving on
to Java AOT annotation processing.

## Pass 16 — PostgreSQL retrieval + JDBC SQL boundary parity

Compared the Java PostgreSQL/SQL provider surface against the current C# provider. The C# SQL provider exposes SQL query writing, SQL execution, PostgreSQL retrieval, and separate retrieval strategies including Fuzzy, FullText, Search, GraphSimilarity, with Vector delegated to the pgvector provider.

Ported the remaining Java gaps:

- PostgreSQL retrieval now fails closed when an optional strategy is disabled instead of silently returning an empty candidate set.
- `GraphSimilarity` now mirrors the C# Apache AGE path: relationship/reference validation, `LOAD 'age'`, Cypher construction, graph execution, graph-similarity evidence and score handling.
- PostgreSQL full-text retrieval now uses the configured PostgreSQL text-search configuration as a SQL literal, matching the C# provider contract.
- Retrieval field resolution now supports an omitted field by selecting the first non-sensitive/string storage field, matching the C# fallback behavior.
- `Vector` remains explicitly delegated to the pgvector semantic lexical provider rather than being implemented in the relational retrieval boundary.
- Added `JdbcSqlPlaceholderRewriter` so the deterministic `@pN`, `@authN`, and execution-context placeholders emitted by the SQL compiler are converted to JDBC positional `?` placeholders only at the physical execution boundary. This preserves provider-plan diagnostics while making `PreparedStatement` execution valid.
- Mutation execution now uses the same placeholder rewriter instead of a mutation-only `@pN` conversion.
- Added regression tests for placeholder rewriting and SQL-literal safety.

The C# retrieval provider intentionally keeps retrieval advisory: authorization and final relational execution remain owned by Foundgine's semantic/execution pipeline.

## Pass 17 — SQL execution boundary and cancellation parity

Compared the Java `SqlExecutionProvider` against the current C# SQL execution provider. The
C# boundary opens the provider connection when necessary, binds runtime execution-context values,
fetches one extra row for forward pagination, materializes cells from column bindings, and emits
execution evidence. The Java implementation already mirrored the binding, pagination, materialization,
and provenance checks, but its physical statement boundary did not register a live cancellation
callback.

The Java SQL execution provider now registers `CancellationToken` against the live
`PreparedStatement` and calls `PreparedStatement.cancel()` on cancellation. Cancellation is also
checked before binding, before execution, during row materialization, and after result/page
construction. This brings ordinary SQL query execution in line with the cancellation behavior
already present in the PostgreSQL mutation provider.

The provider also supports explicit transaction ownership for callers that need a provider-owned
JDBC transaction, while the default constructor remains caller-transaction-neutral. Existing
caller-owned transactions are never committed or rolled back by the default read provider.

Verification:
- JDK 21 syntax/compilation checks were performed on the changed provider source.
- Full Maven/Surefire execution remains unavailable because Maven and external dependency artifacts
  are not installed in this sandbox.
- The C# `SqlExecutionProvider.cs` remains the behavioral reference for execution/result semantics.

Next provider step: add/finish SQL compiler regression coverage for cursor pagination, relationship
joins, aggregate filters/order, authorization predicates, and execution evidence, followed by
opt-in real PostgreSQL E2E coverage for the complete read path.

## Pass 18 — SQL compiler regression parity

Ported the first concrete SQL compiler regression layer from the C# E2E suite into the Java
provider tests. `SqlCompilerParityTest` now locks the provider boundary for:

- aggregate Count filters remaining `COUNT(*)` when no optimizer strategy is present;
- optimized `COUNT > 0` lowering to `EXISTS` without retaining `COUNT(*)`;
- exact-count comparisons such as `COUNT > 1` remaining count-based;
- relationship `SOME` filters lowering to correlated `EXISTS` rather than a top-level join;
- relationship `ALL` filters lowering to `NOT EXISTS` with a negated predicate;
- schema-qualified storage names being quoted as separate SQL identifiers;
- limit/offset values remaining execution-context parameters rather than SQL literals.

This is intentionally provider-level coverage: it verifies that the semantic planning artifact is
lowered correctly without requiring a live database. The next step is to extend the same parity
layer to compound cursor seek predicates, aggregate ordering, authorization predicate rendering,
projection bindings, and then exercise the resulting plans against real PostgreSQL.

## Pass 19 — SQL cursor and authorization boundary parity

Continued SQL provider parity from the C# implementation. Added provider-level regression coverage for compound keyset pagination and the AOT authorization predicate SQL boundary. The new tests verify primary-key cursor tie-breaking, lexicographic seek predicates for ascending/descending order, cursor arity fail-closed behavior, resource/context authorization parameterization, boolean predicate parentheses, and temporal/decimal cursor conversion. No live PostgreSQL dependency is required for these contracts.

## Pass 20 — PostgreSQL read-path E2E parity

Added an opt-in real PostgreSQL read-path suite at `foundgine-providers/src/test/java/com/foundgine/providers/storage/sql/SqlExecutionPostgresE2ETest.java`.

The suite exercises the complete provider boundary rather than only inspecting SQL strings:

- semantic plan -> Execution IR -> `SqlCompiler` -> `SqlPlan` -> JDBC execution;
- authorization provenance remains attached to the provider plan;
- resource/context authorization values are parameterized and supplied only through `ExecutionContext`;
- relationship `SOME` filtering executes as PostgreSQL `EXISTS`;
- actual PostgreSQL rows are materialized into `ExecutionRow` values and semantic cells;
- execution evidence is emitted by the SQL provider;
- cursor pagination performs a real first-page/end-cursor/second-page round trip;
- cursor ordering retains the primary-key tie-breaker;
- tests create and drop isolated schemas and remain skipped unless `FOUNDGINE_POSTGRES_CONNECTION_STRING` is configured.

This closes the gap between SQL compiler regression tests and real PostgreSQL read execution while keeping the provider boundary provider-independent until the SQL/JDBC stage.

## Pass 21 — PostgreSQL nested relationship / quantifier E2E parity

- Extended `SqlExecutionPostgresE2ETest` with real PostgreSQL coverage for nested relationship projections.
- Verifies provider execution of root + child `INNER JOIN` materialization while root authorization remains parameterized.
- Added real PostgreSQL `SOME` + `NONE` composition coverage and verifies both `EXISTS` and `NOT EXISTS` are emitted.
- Added `ALL` relationship quantifier execution coverage and aggregate `COUNT` relationship filtering coverage.
- The tests deliberately assert that semantic comparison values are not embedded in generated SQL.
- Seed data now includes multiple child rows for one parent so nested projection and quantifier semantics are exercised against one-to-many cardinality rather than only one-to-one samples.
- Full Maven execution remains environment-dependent; tests are opt-in through `FOUNDGINE_POSTGRES_CONNECTION_STRING`.

## Pass 22 — PostgreSQL aggregate ordering and cursor parity — 2026-09-12

Added real PostgreSQL E2E coverage for aggregate relationship ordering combined with cursor pagination.

- `COUNT(*)` over a one-to-many relationship can be used as the primary ordering term.
- Cursor ordering automatically retains the root primary key as a deterministic tie-breaker.
- Aggregate cursor values are materialized as hidden SQL selections and encoded into the page cursor.
- The cursor seek predicate preserves mixed directions (`COUNT DESC`, root primary key `ASC`).
- Authorization remains parameterized and is applied before the aggregate ordering result is paged.
- A zero-count relationship remains a valid cursor value because PostgreSQL `COUNT(*)` is non-null.

This closes the next concrete PostgreSQL read-path gap after nested relationship projection, relationship quantifiers, and aggregate filtering.


### Pass 23 — PostgreSQL high-assurance mutation security contract parity

- Added the Java `PostgresMutationSecurityConformance` contract for the high-assurance TransferFunds fixture.
- Mirrored the C# fail-closed contract: transaction atomicity, deterministic row locking, execution-time authorization, idempotency serialization/persistence, replay protection, audit persistence, execution receipt, ownership, daily limit, and tenant isolation.
- Added `PostgresMutationSecurityConformanceParityTest` covering the complete required invariant set and removal of each obligation.
- Expanded the shipped `postgres-transfer-funds` security profile with row-locking, ownership, daily-limit and read-committed transaction invariants.
- This pass deliberately does not claim that a security profile grants runtime authority; the concrete PostgreSQL executor must still prove the guarantees through its execution path and E2E tests.

## Pass 24 — PostgreSQL high-assurance physical TransferFunds executor

- Ported the actual PostgreSQL `TransferFunds` execution adapter into the Java benchmark fixture instead of stopping at a security-profile declaration.
- Added JDBC `READ COMMITTED` transaction ownership, PostgreSQL advisory transaction locks for idempotency keys, deterministic `FOR UPDATE` account locking, replay binding, tenant/ownership/frozen/daily-limit/available-funds validation, authorization-context locking, authorization evidence binding, commit-gate revalidation, atomic debit/credit/idempotency/audit CTE mutation, rollback-on-fault behavior, and execution receipts.
- Added opt-in real PostgreSQL E2E coverage for successful transfer, idempotent replay, injected post-mutation rollback, and authorization revocation at the commit gate.
- Kept the physical adapter below the application-facing service boundary; semantic authorization remains execution-time evidence rather than plan authority.
- Full Maven/live PostgreSQL execution remains environment-dependent on `FOUNDGINE_POSTGRES_CONNECTION_STRING` and Maven/dependency availability.

## Pass 26 — PostgreSQL batch mutation conformance hardening

- Extended the Java `PostgresBatchedMutationCompilerParityTest` with the remaining C# batch-safety guards: mixed insert/update mutation shapes for one entity must fall back to sequential execution; a DELETE operation cannot act as a generated-identity dependency source; and an empty mutation batch is rejected rather than silently producing a physical statement.
- This keeps the Java `tryCompile` contract fail-closed: the PostgreSQL one-statement optimization is used only when the compiler can preserve dependency/correlation semantics safely.

## Pass 27 — authorization authority / execution-binding parity

Mirrored the next high-assurance authority boundary from C#:

- Added `AuthorizationExecutionBinding` to the Java HighAssurance PostgreSQL execution fixture.
- Binding now covers actor, tenant, operation, source/destination resources, amount, idempotency key, authorization version and authorization fingerprint.
- Denied or fingerprint-less authorization evidence cannot be bound.
- Execution-time validation recomputes the binding and fails closed on request/evidence drift.
- `PostgresTransferFundsExecutor` now uses the typed binding rather than a private ad-hoc fingerprint helper.
- Added eight execution-binding parity tests covering exact binding, amount/key/resource/actor/tenant drift, authorization version/fingerprint drift, and denied evidence.
- Added ten runtime authorization-integrity lifecycle parity tests covering rotation, verification-only state, retirement, persisted-evidence protection, monotonic/replay-safe rotation, retired-key reactivation, operator authorization, concurrent rotation, atomic snapshots and invalid provenance.

This closes an important gap identified by the C# `Foundgine.Security.Authority.Tests` surface: authorization evidence is now explicitly bound to the exact physical mutation request instead of being represented only by a free-form fingerprint at the executor boundary.

## Pass 28 — Extensions boundary and Java CI parity

- Added `foundgine-extensions` as the Java counterpart to `Foundgine.Extensions`.
- Kept the boundary optional: Core and Runtime remain transport/framework independent.
- Added deterministic `FoundgineExtensionRegistry` and `TransportExtension` SPI.
- Added duplicate-id and fail-closed lookup tests.
- Added `.github/workflows/java-build.yml` for Java 21 Maven verification on `main`/`java`, with an explicit opt-in PostgreSQL E2E workflow dispatch flag.
- This pass deliberately does not pull GraphQL framework dependencies into Core/Runtime; concrete transport adapters remain optional extensions.

## Pass 30 — MCP agent-client parity

Ported the missing provider-neutral MCP agent-client workflow from C#:
capability discovery, dynamic intent construction, canonical `tools/call` JSON-RPC,
SSE payload extraction, MCP error/result handling, and transport-only security
boundary. The Java client deliberately removes the `security` member from the
serialized `ReadIntent` so host authorization context cannot be manufactured by
the agent client. Added three parity tests covering discovery/execution, SSE
unwrapping, and JSON-RPC errors.
