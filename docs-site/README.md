# Foundgine website

Static public documentation for Foundgine **3.4.0 · .NET 10 and Java 27**.

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
9. **App spec form** — fill in one form describing a domain; it produces a prompt for an AI to generate a Foundgine application.
10. **Packages** — choose packages by architectural responsibility, including the TypeScript, Python and WebAssembly client SDKs.

The main concept pages include an in-page table of contents and previous/home/next navigation. Benchmark and run-result pages use a separate evidence layout. Deeper implementation detail belongs in the linked repository Markdown files, including the Java 27 build guide at `src/java/BUILDING.md` and the shared technical reference under `docs/`.

## Content rules

- Describe the current implementation, not historical milestones.
- Keep the landing page concise; move detailed explanations to dedicated pages or repository docs.
- Distinguish measured benchmark results from modeled estimates.
- Keep security claims tied to executable tests or documented invariants.
- Keep transport, semantic and provider responsibilities explicit.
- Describe Java 27 and .NET 10 as language implementations of the same semantic execution architecture, while clearly marking language-specific capability differences.
- Use the canonical architecture as the stable visual reference.

## Diagrams

Most website diagrams have PlantUML (`.puml`) sources beside their page assets. Some older SVGs under `docs/assets/` and `docs/diagrams/` have no source in this snapshot. Do not hand-edit SVG output; restore or create the corresponding `.puml` source before changing those diagrams and regenerate the SVG.

## Editing

The served HTML is the public rendering source. The smaller Markdown files beside major pages are concise context/reference versions. When adding a public page:

1. use the shared site shell and navigation;
2. add meaningful section headings so the automatic TOC can index them;
3. link to the next conceptual page and the relevant source Markdown;
4. add public/indexable pages to `sitemap.xml` and `llms.txt`;
5. verify relative links before committing.


## App spec form (embeddable component)

`assets/spec-form.js` is a dependency-free component that renders inside a Shadow DOM, so site CSS cannot affect it and it cannot affect the site. `spec-form/index.html` is the public page. To embed it on any page:

```html
<div id="foundgine-spec-form"></div>
<script src="../assets/spec-form.js"></script>
```

Any element with `id="foundgine-spec-form"` or a `data-foundgine-spec-form` attribute is mounted. It inherits the site tokens (`--surface`, `--ink`, `--signal`, `--muted`, `--line`, `--gate`) and can be overridden with `--fsf-accent`, `--fsf-bg`, `--fsf-panel`, `--fsf-ink`, `--fsf-mute`, `--fsf-line`, `--fsf-warn` and `--fsf-font`. Set `--fsf-sticky-top` to the height of a sticky header, and `data-theme="light"` or `"dark"` to force a theme. The section list and the generated AI prompt live in the `SECTIONS` array and `PREAMBLE` string in the script; the prompt adapts to the chosen language (.NET, Java or both) and its Foundgine rules mirror `Foundgine.SupplyChain.Advanced` and `foundgine-supply-chain-advanced`; update them when those samples' conventions or the provider and extension catalogue (`docs/wiki/providers.md`, `extensions.md`) change. Drafts are kept only in the viewer's own browser (`localStorage`); nothing is sent anywhere.

## Benchmark evidence publication

The public `/performance/` page links to two distinct evidence streams: the AgentEndToEnd explorer and the CoffeeBeanery API/database reports. AgentEndToEnd reports are copied and aggregated by `src/csharp/benchmarks/AgentEndToEnd/publish-all-reports.ps1`. CoffeeBeanery report artifacts are copied and indexed by `src/csharp/benchmarks/CoffeeBeanery.Performance/publish-site-reports.ps1`. Do not hand-edit generated files under `docs-site/assets/coffee-beanery-benchmark/`; rerun the publisher after report changes. The CoffeeBeanery report index describes checked-in historical artifacts, not a live feed.
