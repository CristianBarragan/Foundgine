# Foundgine.Core.Semantic.Planning

`Foundgine.Core.Semantic.Planning` is Foundgine's provider-independent planning layer.

## What is in this package

The package contains:

- `Planner` and read `SemanticPlan` construction;
- `MutationPlanner`, `MutationPlan`, nested and batched mutation plans;
- semantic plan nodes and logical operations;
- `SemanticOperationAlgebra`;
- authorization/contract binding through `SemanticPlanAuthorizationBinding`;
- `SemanticPlanFingerprint`;
- optimization and rewrite rules for predicates, projections, relationship traversal, aggregates and cardinality;
- provider-aware advisory cost estimates and rule-selection policies;
- semantic-equivalence and authorization-preservation proofs;
- mutation dependency graphs and execution-level planning.

## Boundary

The planner describes **how an authorized semantic operation may be executed logically**. It does not emit SQL, database aliases, indexes, joins, or transport objects.

```text
Authorized semantic operation
        ↓
SemanticPlan
        ↓
security-preserving optimization
        ↓
ExecutionIR
        ↓
provider compiler
```

Optimization is not allowed to detach a plan from the semantic contract or authorization provenance that produced it.

## Install

```bash
dotnet add package Foundgine.Core.Semantic.Planning
```
