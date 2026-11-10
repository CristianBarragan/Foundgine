# Building Foundgine Java

## Requirements

- JDK 27 (the project compiler target is Java 27).
- Apache Maven 3.9 or newer.

Java 27 is the latest feature release as of November 2026. Java 25 is the current
LTS release; use Java 25 instead only if you intentionally want an LTS-compatible
baseline, and set `maven.compiler.release` to `25` in the root POM.

## Build the complete Maven reactor

Run all Maven commands from this directory (the directory containing this
`pom.xml`):

```bash
mvn -U clean verify -Dgpg.skip=true
```

The root reactor includes `foundgine-providers` and the sample applications.
That is intentional: the generator is a build-time annotation processor and is
not expected to be downloaded from Maven Central as a separate published
library. The supply-chain sample consumes it through the reactor using the
coordinate `io.github.cristianbarragan:foundgine-providers:${revision}`.

To build only the supply-chain sample and all of its reactor prerequisites:

```bash
mvn -U -pl samples/foundgine-supply-chain -am verify -Dgpg.skip=true
```

Do not run the sample's build in isolation against Maven Central before building
the reactor: the AOT generator is deliberately not a separately published
runtime dependency.

`-Dgpg.skip=true` is useful for local verification when signing keys are not
configured. Omit it in a release workflow that has the required signing
credentials configured.

## ORM / JPA parity work

The current Java JPA adapter is a partial implementation baseline. Its staged
parity programme, exact feature gaps, database targets, and release gates are
maintained in [`ORM-JPA-PARITY-PLAN.md`](ORM-JPA-PARITY-PLAN.md). Validate the
machine-readable plan with:

```bash
python conformance/validate-orm-jpa-parity-plan.py
```
