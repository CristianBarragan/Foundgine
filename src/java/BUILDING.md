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

The root reactor includes the core libraries, providers, migrations, optional
Spring Boot and LangChain4j integrations, the build-time generator, security
tests and sample applications. The AOT generator is excluded from standalone
publication; the Supply Chain sample resolves its annotation processor through
`foundgine-providers` in the reactor using the coordinate
`io.github.cristianbarragan:foundgine-providers:${revision}`.

The Spring Boot and LangChain4j modules have source and test definitions, but
their dependency pins and runtime verification remain unverified in this
snapshot. Do not describe those integrations as runtime-certified.

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
maintained in [`conformance/orm-jpa-parity-plan.json`](conformance/orm-jpa-parity-plan.json), with validation in [`conformance/validate-orm-jpa-parity-plan.py`](conformance/validate-orm-jpa-parity-plan.py). Validate the
machine-readable plan with:

```bash
python conformance/validate-orm-jpa-parity-plan.py
```
