# Conformance and shared test data

Foundgine keeps reusable cross-language test vectors and protocol scenarios in
[`src/conformance/`](https://github.com/Foundgine/Foundgine/blob/main/src/conformance). These files are consumed by the
language-specific test suites so that shared contracts can be checked without
copying every fixture into each implementation.

The collection currently includes scenarios and vectors for:

- approval envelopes and warrant canonicalization/signatures;
- delegation and tenant-first authorization gates;
- MCP approval tokens, sessions and server-sent events;
- semantic query behavior.

The Java ORM/JPA capability plan is maintained separately in
[`src/java/conformance/orm-jpa-parity-plan.json`](https://github.com/Foundgine/Foundgine/blob/main/src/java/conformance/orm-jpa-parity-plan.json)
and validated by the adjacent Python script. A plan or fixture records intended
coverage; it is not evidence that a test has passed. Use the language-specific
CI workflows and test results to establish verification status.

See [`src/conformance/README.md`](https://github.com/Foundgine/Foundgine/blob/main/src/conformance/README.md) for the fixture
conventions and generation tools.

---

Next: [Getting started](GETTING-STARTED.md)
