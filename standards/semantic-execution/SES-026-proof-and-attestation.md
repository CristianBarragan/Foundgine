# SES-026 — Proof, Attestation, and Verification

**Status:** Draft 1.0 — Normative target.

## 1. Purpose

Security claims become meaningful only when executable artifacts carry verifiable evidence connecting semantics, authority, planning, and provider guarantees.

## 2. Proof objects

A future proof object MUST be able to bind, at minimum:

- canonical contract identity;
- canonical SOG identity;
- authorization decision and policy snapshot;
- provenance identity;
- plan/IR identity;
- provider capability identity;
- resource budget;
- validity interval;
- signer/attester identity where cryptographic attestation is used.

## 3. Verification

Verification MUST occur before the execution boundary. Verification MUST fail closed on malformed, incomplete, stale, unverifiable, or incompatible proof material.

## 4. Rewrite certificates

A security- or meaning-relevant optimizer rewrite MUST provide a certificate or independently checkable derivation showing why the transformed artifact remains within the authorized semantic envelope.

## 5. Cryptography

Where cryptographic integrity is required, implementations MUST define canonical input bytes, algorithm identifiers, key identifiers, trust anchors, rotation, revocation, and algorithm-agility rules. Cryptographic signing of an ambiguous serialization is non-conformant.

## 6. Future development

Define portable proof formats, proof composition, signature suites, key lifecycle, revocation, transparency/audit mechanisms, and lightweight proof verification suitable for constrained runtimes.

## 7. Acceptance

Mutating any meaning- or authority-relevant byte MUST invalidate verification. A stale policy, altered provider capability, altered IR, or altered contract MUST prevent execution.
