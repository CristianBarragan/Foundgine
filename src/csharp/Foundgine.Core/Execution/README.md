# Foundgine.Core.Execution

`Foundgine.Core.Execution` is the provider execution boundary between logical Foundgine plans and physical providers.

## What is in this package

### Provider contracts

- `IExecutionProvider`
- `IProviderPlanCompiler`
- provider security-conformance contracts
- provider plan cache contracts and `MemoryProviderPlanCache`

### Runtime representation

- `ExecutionIR`
- `ExecutionIRBoundary`
- `ProviderPlan`
- `ExecutionContext`
- `ExecutionContextKeys`

### Security

- `SecurityInvariantExecutionGate`
- `SecurityInvariantProofGate`
- `SecurityInvariantAttestation`
- `SecurityInvariantProof`
- provider security conformance
- `IExecutionAuthorizationRevalidator`
- semantic authorization revalidation

### Results and evidence

- `ExecutionResult`
- `MaterializedResult`
- `ResultMaterializer`
- `ExecutionReceipt`
- `ExecutionEvidence`

### Mutation execution

- mutation execution providers and batch providers;
- `ExecutionMutationIR`;
- mutation dependency graphs and execution levels;
- mutation security conformance;
- mutation result/materialization contracts.

## Boundary

Providers receive provider-specific plans only after Foundgine has established the required semantic and security obligations. A provider must satisfy the execution/security contract before execution proceeds.

This package contains no SQL implementation and no GraphQL/MCP transport implementation.

## Install

```bash
dotnet add package Foundgine.Core.Execution
```
