# SES-016 — Resource Limits, Complexity, and Denial-of-Service Controls
← [SES-015 — Transport, Agent, and Tool Boundaries](SES-015-transport-and-agents.md) · [Standard Index](README.md) · [SES-017 — Execution Evidence, Errors, and Observability](SES-017-evidence-and-observability.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Semantic systems can amplify tiny inputs into enormous retrieval, graph, planning, or execution workloads. Resource governance MUST therefore begin before provider execution.

## 2. Required budgets

A mature implementation SHOULD budget:

- input size;
- graph nodes/edges;
- traversal depth/fan-out;
- candidate count;
- resolver work;
- planner work;
- provider cost;
- rows/results;
- memory;
- CPU;
- wall time;
- concurrency;
- mutation steps;
- agent/tool calls.

## 3. Critique

Static limits alone are insufficient. The future standard needs **adaptive cost estimation, tenant fairness, priority, backpressure, cancellation propagation, and budget accounting across distributed components**.

## 4. Acceptance criteria

Every executable operation MUST have a bounded resource envelope or an explicit administrative profile permitting unbounded behavior. Exceeding a src/csharp/security/resource budget MUST stop expansion safely.

---

← [SES-015 — Transport, Agent, and Tool Boundaries](SES-015-transport-and-agents.md) · [Standard Index](README.md) · [SES-017 — Execution Evidence, Errors, and Observability](SES-017-evidence-and-observability.md) →
