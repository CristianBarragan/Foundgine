# SES-015 — Transport, Agent, and Tool Boundaries

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

MCP, GraphQL, REST, RPC, SDKs, command interfaces, and AI agents MUST converge on the same semantic execution model rather than create parallel authorization paths.

## 2. Critique

Agentic systems introduce threats not present in ordinary APIs:

- prompt injection;
- tool-description injection;
- malicious metadata;
- confused-deputy tool selection;
- hidden tool composition;
- unbounded planning loops;
- authority laundering through natural language;
- cross-session memory contamination.

The standard needs an explicit **agent authority model** and should treat tool discovery as untrusted input until validated against the trusted semantic contract.

## 3. Future development

Define transport-neutral request envelopes, tool capability manifests, agent/session identity, delegation, approval, tool-call budgets, provenance propagation, and cross-transport equivalence tests.

## 4. Acceptance criteria

The same authorized semantic request delivered through different transports MUST result in equivalent authorization semantics. A transport-specific shortcut MUST NOT bypass the canonical security pipeline.

