# Foundgine Repository Cleanup Audit

## Scope and method

Cleanup was performed on the uploaded ZIP snapshot (`Foundgine.zip`), not on a Git working tree. The archive was integrity-tested before extraction. The extracted snapshot contained 3,626 files and occupied approximately 335 MB before cleanup.

The cleanup was conservative: files were removed only when they were clearly generated caches, temporary backups, captured console logs, or an error-output artifact. Duplicate content was reviewed but not bulk-deleted because many duplicates are intentional source/site mirrors, cross-language parity files, fixtures, benchmark evidence, or package resources.

## Removed

- Python bytecode and `__pycache__` directories under `tools/foundgine-perf` (4 `.pyc` files).
- One Java `.bak` backup: `AdvancedMutationPipeline.java.bak`.
- Captured benchmark console logs named `agent-benchmark-console.log` in AgentEndToEnd Run1–Run3 artifact directories (14 files).
- `src/csharp/benchmarks/AgentEndToEnd/build-errors.txt`, a captured MSBuild error output indicating a missing solution file; it is not source or test input.

## Retained intentionally

- Benchmark JSON/CSV/Markdown results and published copies in `docs-site/assets`: these are evidence and site content, not disposable build outputs.
- Generated code, schema snapshots, migrations, test fixtures, assets, and package metadata: these may be required by builds, tests, releases, or documentation.
- Identical files across C#/Java, source/docs/site, package licenses, and benchmark variants: these were not removed without checking their role.
- GitHub workflows, project/build configuration, release files, and hidden configuration.

## Validation performed

- ZIP integrity: `unzip -t` passed.
- Repository inventory and suspicious artifact scan.
- Exact-content duplicate scan; duplicates were reviewed as likely intentional copies/parity artifacts and preserved.
- Basic local-link scan over Markdown/HTML/YAML. The scan reported 116 apparent misses among 1,243 references, but the scanner does not fully understand extensionless GitHub Wiki links, MkDocs resolution, anchors, template expressions, or URL rewriting. These results are therefore **not a validated broken-link count** and require a proper MkDocs/Wiki-aware validation pass.
- No full C#, Java, Python, TypeScript, WASM build/test suite was run during this cleanup. This report does not claim build or test validation.

## Follow-up recommended

1. Run the repository's documented build/test matrix in a clean environment.
2. Run `mkdocs build --strict` with the repository's documented dependencies.
3. Validate GitHub Wiki links using GitHub Wiki page naming rules and check website HTML navigation/routes.
4. Review the benchmark artifact layout and documentation mirroring policy before any further deduplication.
5. Verify package/release workflows against the retained configuration.
