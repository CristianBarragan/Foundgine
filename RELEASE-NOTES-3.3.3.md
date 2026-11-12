# Foundgine 3.3.3 (.NET 10 and Java 27)

Patch release on the aligned .NET / Java 3.3.x line.

> **Status: prepared, not yet release-verified.** The repository metadata and package workflow have been updated, but this environment does not have the .NET SDK or Maven installed. Do not create the `v3.3.3` tag or publish packages until the release gates below pass in CI or on a machine with the required toolchains.

## Packaging changes

- `Foundgine.Providers` is the single consumer-facing NuGet package for the provider integrations. It packages the Foundgine-owned `Foundgine.Providers.Orm.dll` and `Foundgine.Providers.EntityFrameworkCore.dll` alongside `Foundgine.Providers.dll`.
- The Foundgine AOT and ORM source generators are packaged under `analyzers/dotnet/cs/`; neither generator is a runtime library asset.
- `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, and `Microsoft.Extensions.DependencyInjection.Abstractions` are declared as NuGet dependencies at version `10.0.0`. NuGet restores these third-party dependencies for consumers; their DLLs are not copied into the Foundgine package.
- The package-validation workflow checks the expected Foundgine-owned runtime assemblies, both analyzer assets, EF Core dependency declarations, and absence of bundled EF Core / DI DLLs.
- Internal project boundaries remain intact. The package is assembled from the existing provider, ORM, EF Core and generator projects rather than merging their source into one project.

## Version and documentation alignment

- Set the canonical .NET `VersionPrefix` in `Directory.Build.props` to `3.3.3`.
- Set the Java Maven reactor `<revision>` to `3.3.3`; committed flattened POM metadata is aligned to the same version.
- Align MCP manifests, NuGet / Maven installation snippets, current-status documentation, website labels, AI reference files and CI metadata to `3.3.3`.
- Replace obsolete product-generation wording (“Foundgine v2”) in current package descriptions and READMEs with “Foundgine 3.x”. Historical protocol identifiers such as `plan-v2`, envelope versions and historical release notes are intentionally unchanged.
- Preserve benchmark run labels that identify the version actually measured; historical benchmark snapshots are not relabelled as 3.3.3 evidence.

## Carried-forward capabilities

This patch does not claim new semantic or JPA capability completion. The `SemanticQuery` and EF reader work described in [3.3.1 release notes](RELEASE-NOTES-3.3.1.md) remains part of the source baseline. Java JPA capabilities remain subject to their own implementation and test gates; source presence or package metadata is not evidence that H2, PostgreSQL or Native Image integration tests pass.

## Release gates (required before tagging `v3.3.3`)

1. **.NET:** run `dotnet restore src/csharp/Foundgine.sln`, `dotnet build src/csharp/Foundgine.sln --configuration Release`, and the full test suite.
2. **NuGet package:** run `dotnet pack src/csharp/Foundgine.sln --configuration Release`; inspect `Foundgine.Providers.nupkg` and verify the three Foundgine runtime assemblies, both analyzer DLLs, EF Core / relational / DI dependencies in the `.nuspec`, and no bundled third-party EF Core / DI DLLs.
3. **Java 27:** run the full Maven reactor from `src/java/pom.xml` with Java 27, including unit and PostgreSQL integration tests as configured by CI.
4. **Version consistency:** confirm `Directory.Build.props`, `src/java/pom.xml`, MCP manifests, current installation instructions and package metadata all resolve to `3.3.3`. Non-release CI versions may carry their documented run suffix.
5. **Documentation:** rebuild the MkDocs site and verify generated HTML / AI reference outputs against the Markdown sources.
6. Inspect successful hosted GitHub Actions evidence for both .NET and Java workflows. A workflow definition or local static check is not a passing build/test result.
7. Only after all gates pass, create tag `v3.3.3` and publish the four NuGet packages and the Java Maven artifacts.
