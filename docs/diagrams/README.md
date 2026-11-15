# Diagrams in this directory

> **Which copy is authoritative?** The documentation pages embed diagrams from `docs/assets/`, not from this directory. Nine of the ten SVGs here are byte-identical to their `docs/assets/` copies; `lexical-grounding-flow.svg` here is an older variant without the alias and example notes that the `docs/assets/` version (used by `LEXICAL-GROUNDING.md`) has. Edit the `docs/assets/` source for anything a page displays, and treat this directory as a duplicate snapshot until it is removed or generated from `docs/assets/`.

The SVGs in `docs/diagrams/` are generated assets. PlantUML source (`.puml`) has been recovered from the source text that PlantUML embeds inside each SVG, and sits next to its SVG:

| SVG | PlantUML source |
| --- | --- |
| `canonical-architecture.svg` | `canonical-architecture.puml` |
| `lexical-grounding-budget-exceeded.svg` | `lexical-grounding-budget-exceeded.puml` |
| `lexical-grounding-example-candidates.svg` | `lexical-grounding-example-candidates.puml` |
| `lexical-grounding-example-path.svg` | `lexical-grounding-example-path.puml` |
| `lexical-grounding-flow.svg` | `lexical-grounding-flow.puml` |
| `pgvector-e2e-pipeline.svg` | `pgvector-e2e-pipeline.puml` |
| `retrieval-strategy-boundary.svg` | `retrieval-strategy-boundary.puml` |
| `grounding-decision-lifecycle.svg` | `grounding-decision-lifecycle.puml` (decoded from the `SRC=` payload PlantUML stores in the SVG; `@startuml`/`@enduml` wrapper re-added) |
| `lexical-grounding-example-schema.svg` | `lexical-grounding-example-schema.puml` (a `@startmindmap` diagram) |

`lexical-grounding-alias-weight.svg` has no PlantUML source because it is a hand-written SVG; edit the SVG directly.

The same recovery was applied to `docs/assets/` (11 `.puml` files).

Do not hand-edit SVG output. Change the `.puml`, regenerate the SVG with PlantUML, and keep both in sync. The recovered sources have not been re-rendered in this environment (PlantUML is not installed), so confirm a clean render before committing a regenerated SVG.

## Numbered `*-plantuml-NN.svg` files

The 83 files in `docs/assets/` named like `security-plantuml-05.svg` describe themselves as "PlantUML diagram converted to standalone SVG". They are plain SVGs with no embedded PlantUML, and the original `.puml` files are not in this repository. To change one, edit the SVG, or redraw it in PlantUML and replace the file.
