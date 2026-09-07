# History

A short project timeline. For version-by-version detail, see the root
[`CHANGELOG.md`](../../CHANGELOG.md).

- **V2 baseline** — the architecture was reworked around the current
  semantic execution model (semantic contract, operation graph, planning,
  provider boundary) rather than a thinner query-translation layer.
- **Stable baseline / v0.2.x** — a NuGet release line was established.
- **MCP integration** — Foundgine gained a Model Context Protocol provider so
  agent tooling could consume semantic intents directly.
- **v0.3.0** — first tagged release after the MCP work landed.
- **Security hardening** — the security execution boundary, execution
  context, and security warrant model were added, followed by penetration
  testing and a v0.5.x line.
- **v1.0.0** — first 1.0 release.
- **2.0.x** — current release line; see `CHANGELOG.md` for the active
  per-release detail (semantic grounding, resolver diagnostics, and test
  coverage additions).

This page intentionally stays high-level; day-to-day changes belong in
`CHANGELOG.md`, not here.

---

Next: [Roadmap](../ROADMAP.md)
