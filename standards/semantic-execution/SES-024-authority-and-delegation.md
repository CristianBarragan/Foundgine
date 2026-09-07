# SES-024 — Authority Algebra, Delegation, and Attenuation

**Status:** Draft 1.0 — Normative target.

## 1. Purpose

A mature SES MUST distinguish authentication, authorization, delegation, approval, and execution-time revalidation. These concepts MUST NOT be represented by one opaque boolean.

## 2. Authority tuple

Authority SHOULD be modeled as a structured tuple containing principal, action/capability, resource scope, tenant scope, purpose, classification constraints, temporal validity, environmental constraints, obligations, and delegation lineage.

## 3. Delegation

Delegation MUST be explicit, bounded, attributable, and attenuable. A delegate MUST NOT acquire authority outside the delegator's authority.

Formally:

`Authority(delegate) ⊆ Authority(delegator)`

unless a separate trusted authority grants additional rights.

## 4. Attenuation

Delegation MAY narrow actions, resources, tenants, time, purpose, or budgets. It MUST NOT widen them.

## 5. Revocation and freshness

A system MUST define how revoked or expired authority is detected before execution. Long-lived delegated authority MUST NOT be treated as permanently valid.

## 6. Human approval

Where approval is required, approval MUST bind to a canonical semantic artifact or digest. Approving one operation MUST NOT implicitly approve a materially different operation.

## 7. Future development

Define authority algebra operators, delegation token format, attenuation rules, approval protocol, revocation protocol, and proof verification.

## 8. Acceptance

Adversarial tests MUST attempt privilege widening through delegation, approval substitution, scope substitution, stale tokens, and replay. All widening attempts MUST fail.
