# Documentation verification checkpoints

- **Checkpoint 1**: MkDocs `--strict` build passes. 31 links to files outside `docs/` rewritten to GitHub URLs; 3 extensionless wiki links fixed.
- **Checkpoint 2**: source-vs-docs audit for all five languages; two wrong class names fixed; see `VERIFICATION-REPORT.md`.
- **Checkpoint 3**: security limits, approvals, audit, MCP tool names, badges, website links and changelog claims verified against source; no further doc errors found.
- **Checkpoint 4**: added `wiki/mcp-protocol-and-tokens.md` (nav entry under AI agents and MCP).
- **Checkpoint 5**: recovered 16 PlantUML sources from SVGs; diagrams README updated.
- **Checkpoint 6**: diagram duplication and orphan findings recorded; nothing deleted.
- **Checkpoint 7**: recovered lifecycle (SRC-decoded) and schema (mindmap) sources; documented hand-written and converted SVGs.
- **Checkpoint 8**: embedded `pgvector-e2e-pipeline.svg` in `POSTGRES-E2E.md`. External links still unchecked (no network).
