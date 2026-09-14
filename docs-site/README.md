# Foundgine website

Static public documentation for Foundgine **2.2.1 · .NET 9**.

## Public reading path

The site is a guided product story rather than a dump of repository internals:

1. **What is Foundgine?** — the problem and semantic execution boundary.
2. **Getting started** — run the starter Supply Chain application.
3. **Walkthrough** — trace one request with concrete payloads.
4. **Architecture** — use the canonical lifecycle as the reference map.
5. **AI agents** — understand the agent/tool boundary.
6. **Security** — inspect authorization and fail-closed invariants.
7. **Samples** — choose starter, advanced semantic and PenTest material.
8. **Evidence** — inspect benchmark methodology and results.
9. **Packages** — choose packages by architectural responsibility.

Every conceptual page has an in-page table of contents and a previous/home/next navigation strip. Deeper implementation detail belongs in the linked repository Markdown files.

## Content rules

- Describe the current implementation, not historical milestones.
- Keep the landing page concise; move detailed explanations to dedicated pages or repository docs.
- Distinguish measured benchmark results from modeled estimates.
- Keep security claims tied to executable tests or documented invariants.
- Keep transport, semantic and provider responsibilities explicit.
- Use the canonical architecture as the stable visual reference.

## Diagrams

PlantUML sources and rendered SVGs live beside each other in `assets/` where they are maintained as documentation assets. The AI-agent boundary now uses the same professional visual language as the canonical architecture rather than a compressed mindmap.

## Editing

The served HTML is the public rendering source. The smaller Markdown files beside major pages are concise context/reference versions. When adding a public page:

1. use the shared site shell and navigation;
2. add meaningful section headings so the automatic TOC can index them;
3. link to the next conceptual page and the relevant source Markdown;
4. add public/indexable pages to `sitemap.xml` and `llms.txt`;
5. verify relative links before committing.
