# Core S0 Permit-Verifier Negative Test Matrix

**Work round:** `Core-S0-Producer-01`
**State:** `SKELETON — BLOCKED ON APPROVED CONTRACT/FIXTURES`
**Contract proposal:** `tools/CORE_S0_CONTRACT_PROPOSAL.md`

> This matrix contains no token, key, endpoint, credential, identifier, or raw runtime configuration. Test vectors must use only Security-approved synthetic non-production material. Rows remain blocked until the shared package revision/hash is approved.

## Fixture gate applicable to every row

Before loading a vector, the harness must compare the expected and actual contract revision plus raw 32-byte package SHA-256. Missing or mismatched identity must fail visibly with a sanitized typed failure. No vector may run against an unknown package.

## Negative matrix

| ID | Category | Synthetic mutation | Required Core outcome | Engine start count | Status |
|---|---|---|---|---:|---|
| P-001 | Compact structure | missing/extra segment | typed sanitized authorization failure | 0 | BLOCKED — fixture/schema |
| P-002 | Bounded input | permit exceeds approved bound | reject before crypto processing | 0 | BLOCKED — size decision |
| P-003 | Encoding | malformed base64url | typed sanitized authorization failure | 0 | BLOCKED — parser semantics |
| P-004 | Encoding | invalid UTF-8/JSON | typed sanitized authorization failure | 0 | BLOCKED — parser semantics |
| P-005 | Header | missing/wrong `typ` | typed sanitized authorization failure | 0 | BLOCKED — exact `typ` |
| P-006 | Algorithm | `none`, symmetric, or non-RS256 | typed sanitized authorization failure | 0 | BLOCKED — approved fixture |
| P-007 | Key ID | missing/empty/unknown/retired `kid` | fail closed; no arbitrary-key fallback | 0 | BLOCKED — key policy |
| P-008 | Signature | bad/truncated signature | typed sanitized authorization failure | 0 | BLOCKED — test key/vector |
| P-009 | Critical header | unsupported `crit` | typed sanitized authorization failure | 0 | BLOCKED — header policy |
| P-010 | Duplicate header | duplicate security property | reject | 0 | BLOCKED — duplicate policy |
| P-011 | Required claims | missing required claim | typed sanitized authorization failure | 0 | BLOCKED — claim schema |
| P-012 | Claim type | wrong JSON type | typed sanitized authorization failure | 0 | BLOCKED — claim schema |
| P-013 | Issuer/audience | wrong issuer or audience | typed sanitized authorization failure | 0 | BLOCKED — exact values |
| P-014 | Product/scope | wrong product or scope | typed sanitized authorization failure | 0 | BLOCKED — exact values |
| P-015 | Time | expired at/beyond frozen boundary | typed sanitized expiry/invalid result | 0 | BLOCKED — time policy |
| P-016 | Time | not-yet-valid/future-issued | typed sanitized expiry/invalid result | 0 | BLOCKED — time policy |
| P-017 | Time | lifetime exceeds approved maximum | reject | 0 | BLOCKED — TTL policy |
| P-018 | Binding | wrong challenge | reject and consume according to frozen admission policy | 0 | BLOCKED — admission policy |
| P-019 | Binding | wrong canonical config digest | reject | 0 | BLOCKED — canonical fixture |
| P-020 | Binding | wrong session/install/runtime binding | reject | 0 | BLOCKED — claim/binding policy |
| P-021 | Duplicate claims | duplicate security claim | reject | 0 | BLOCKED — duplicate policy |
| P-022 | Replay | reuse consumed challenge/permit | typed sanitized replay/invalid result | 0 | BLOCKED — external mapping |
| P-023 | Concurrency | two consumers use same attempt | at most one admitted | at most 1 | BLOCKED — integrated verifier |
| P-024 | Key service | resolver unavailable | `AuthorizationUnavailable` or approved collapsed code | 0 | BLOCKED — error mapping |
| P-025 | Leakage | sentinel in public result/log/exception/temp | sentinel absent everywhere | 0 | BLOCKED — executable harness |

## Existing contract-independent harness evidence

The current worktree provides only:

- bounded opaque `SensitivePermit` with a caller-supplied positive bound;
- redacted string rendering;
- typed clock/key-resolver/canonical-serializer/verifier seams;
- fixture revision plus raw SHA-256 identity gate;
- fixed-time package-hash comparison;
- visible sanitized fixture-mismatch failure tests.

These seams are not a JWT parser/verifier and are not evidence for any P-001–P-025 cryptographic or cross-language row.

## Activation rule

A row may change from `BLOCKED` to executable only when its exact schema/policy and a Security-approved synthetic vector exist in the shared package identified by the same revision/hash used by Launcher, Backend, and Core. Production material is prohibited.
