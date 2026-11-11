# CI and release validation

This page describes the repository checks that protect the .NET 10 and Java 27 implementations.

## Canonical release version

`Directory.Build.props` is the canonical .NET package version source. The build workflow parses it as XML (rather than using a shell regular expression), requires `3.3.1`, and rejects a Git tag whose version differs. Java's Maven reactor uses `src/java/pom.xml` with the same release version; CI uses a run-scoped `3.3.1-ci.<run-number>` revision for non-release builds.

## .NET build

The main build workflow restores and builds `src/csharp/Foundgine.sln`, then packs the solution. PostgreSQL/Testcontainers integration checks run in a separate job with PostgreSQL 17.

## Java 27 build

The Java reactor is `src/java/pom.xml`. CI installs Temurin 27 and runs:

```bash
mvn --file src/java/pom.xml \
  --batch-mode --no-transfer-progress --errors \
  -Dgpg.skip=true \
  -Drevision=3.3.1-ci.<run-number> \
  clean package
```

The reactor contains the core, runtime, providers, extensions, red-team verification and runnable samples. The PostgreSQL E2E job runs the reactor's verification lifecycle with Docker available.

## CodeQL

CodeQL uses `build-mode: none` for C# because its workflow does not require a traced compiler build. Java/Kotlin uses a manual build: the workflow installs Java 27 before CodeQL initialization, then builds the Maven reactor with `clean package` and `-DskipTests`. This gives CodeQL the actual compiled Java source set instead of relying on autobuild detection, which does not reliably discover a nested Maven reactor under `src/java/`.

## Interpreting CI evidence

A workflow definition is not evidence of a successful hosted run. Confirm the GitHub Actions run after merging changes. Keep compiler/test failures visible; do not convert them into skipped checks merely to obtain a green workflow.
