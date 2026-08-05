# NekoProxyCore — Core-Owned S0 Contract Proposal

> **ARCHIVED HISTORICAL SNAPSHOT:** เก็บเพื่อ trace history เท่านั้น ดูสถานะปัจจุบันที่
> [`../../current/core-release-handoff.md`](../../current/core-release-handoff.md)

**Work round:** `Core-S0-Producer-01`
**Date:** 2026-08-03
**Repository:** `D:\NekoProxyCore`
**Branch:** `feature/neko-headless`
**Baseline revision:** `8d19f36c3c18be52cc36b0102d1496510fdbd30f`
**Owner:** NekoProxyCore Team
**Reviewers:** Connector, Launcher, Backend, Security
**State:** `PROPOSED — REQUIRES CROSS-TEAM REVIEW`
**Readiness:** `DESIGN READY / IMPLEMENTATION PARTIAL / PRODUCTION BLOCKED`

> Sanitized contract input only. This document contains no production endpoint, token, permit, credential, private key, customer identifier, or raw runtime configuration. Values marked `REQUIRES DECISION` are not approved production values and must not be implemented as compatibility defaults.

---

## 1. Scope and normative language

This proposal defines Core-owned invariants and the exact decisions Core requires from other teams before production implementation. `MUST`, `MUST NOT`, and `SHALL` are proposed normative requirements. A field marked `REQUIRES DECISION` blocks production behavior that depends on it.

Core remains fail closed while S0 is pending:

- no production Named Pipe host;
- no protocol-v1 production start;
- no permit means no `Starting`, controller, engine, driver, helper, or network side effect;
- no allow-all, offline, local-signing, debug, or compatibility bypass;
- no production issuer, audience, key, TTL, endpoint, or credential architecture inferred from examples.

---

## 2. Core challenge contract

### 2.1 Core-owned proposal

| Property | Proposed requirement | Owner/status |
|---|---|---|
| Generator | .NET cryptographic RNG | Core — implemented |
| Entropy | exactly 32 decoded bytes / 256 bits | Core — implemented |
| Encoding | base64url without padding | Core — implemented |
| Encoded size | exactly 43 ASCII characters | Core — proposal derived from 32 bytes |
| Storage | memory only, bound to one Core process instance | Core — proposed |
| Clock | monotonic clock for local lifetime | Core — implemented |
| Maximum lifetime | 30 seconds; expired when elapsed time is greater than or equal to lifetime | Core — implemented maximum |
| Outstanding state | one challenge per host/start state | Core — implemented primitive |
| Replacement | issuing a new challenge invalidates the previous outstanding challenge | Core — implemented primitive |
| Comparison | fixed-time comparison after exact-length validation | Core — implemented |
| Consumption | atomic one-attempt consume | Core — implemented primitive |
| Failed admitted attempt | consumes challenge regardless of permit validation result | Core — proposed |
| Replay | consumed/replaced challenge cannot be reused | Core — implemented primitive |
| Concurrency | at most one concurrent consumer receives acceptance | Core — implemented/tested |
| Restart | challenge from a previous Core process is invalid | Core — proposed by memory-only state |

### 2.2 Admission and retry semantics

Proposed Core order:

```text
bounded frame admission
→ bounded permit structural admission
→ atomically consume challenge attempt
→ key/header/signature/claims/time/binding validation
→ immutable runtime request validation
→ exact target recheck
→ publish Starting
→ controller/engine/driver/network start
```

After a request reaches the frozen authorization-attempt admission point, timeout, disconnect, bad signature, bad claim, wrong configuration, target disappearance, or ambiguous client result MUST require a new challenge and a newly issued permit. Core MUST NOT restore consumed state.

### 2.3 Required decisions

| Decision | Required owner |
|---|---|
| Exact point at which a frame becomes an admitted authorization attempt | Security + Core |
| External mapping for invalid, expired, replaced, and replayed challenge | Security + Launcher + Core |
| Whether a disconnect before complete bounded structural admission consumes state | Security + Core |
| Whether one outstanding challenge is global per host or scoped to an explicit connection/start slot | Launcher + Security + Core |
| Challenge response schema and exact field casing | Launcher + Core |

---

## 3. Core-side protocol v2 validation proposal

### 3.1 Invariants Core requires

- Production protocol version MUST be distinct from checkpoint protocol v1; proposed value is `2`.
- Production host MUST reject protocol-v1 `start`.
- Input MUST be bounded before JSON/token parsing.
- Root MUST be a JSON object encoded as strict UTF-8.
- Command, version, correlation, and security-sensitive fields MUST have exact JSON types.
- Duplicate security-sensitive properties MUST be rejected.
- Unknown properties in `challenge` and `start` requests SHOULD be rejected.
- `start` MUST contain a non-empty bounded opaque permit.
- Correlation ID MUST be bounded, opaque, and non-secret.
- Command processing MUST be single-flight at the runtime boundary.
- Responses MUST be serialized from an allow-list; raw exception messages and decoded permit content MUST NOT cross the boundary.

### 3.2 Proposed command responsibilities

| Command | Core behavior |
|---|---|
| `challenge` | issue/replace one Core challenge and return only bounded challenge metadata |
| `start` | require permit, consume admitted challenge attempt, verify all bindings, recheck target, then start |
| `status` | return typed lifecycle state and allow-listed error code only |
| `stop` | perform bounded deterministic cleanup; repeated stop is idempotent after confirmed cleanup |

### 3.3 Proposed allow-listed response fields

```text
version
kind
correlationId
typed status
succeeded
typed errorCode
challenge and relative lifetime only for challenge response
```

Responses/logs MUST NOT include:

```text
permit or token fragments
decoded headers or claims
expected issuer/audience/challenge/config digest
account/session/license/installation identifiers
endpoint or proxy credential
raw configuration
exception message or stack
```

### 3.4 Required decisions

| Decision | Required owner |
|---|---|
| Exact JSON schemas and property casing | Launcher + Core |
| Framing: length-prefix or delimiter; partial read/write rules | Launcher + Core + Security |
| Maximum frame, permit, segment, correlation, request, and response sizes | Security + Launcher + Core |
| Command/property case sensitivity and unknown-field policy | Security + Launcher + Core |
| Exact timeout values for connect/read/write/challenge/start/status/stop | Launcher + Core |
| Exact pipe name and ACL requirements | Launcher + Security + Core |
| Error-code compatibility/change-control policy | Launcher + Core + Security |

---

## 4. Canonical start-configuration proposal

### 4.1 Security-relevant field set

Core proposes that the canonical configuration contain only immutable, non-secret fields consumed by the same runtime start attempt:

1. protocol version;
2. exact target process identity representation;
3. opaque profile reference;
4. opaque server reference;
5. approved proxy mode representation, if not implied by the product scope.

Credentials, endpoints, local paths, timeout values, correlation IDs, account/session identifiers, and mutable display data MUST NOT be included.

### 4.2 Serialization proposal

- UTF-8 without BOM;
- LF (`0A`) between fields and after the final field;
- fixed field order;
- exact ASCII field names;
- no surrounding whitespace;
- decimal integer representation without leading plus/zero padding where numeric;
- exact lowercase target basename `pso2.exe` if basename-only identity is approved;
- opaque references retain exact validated ordinal bytes;
- SHA-256 over canonical bytes;
- fixed-time digest comparison.

Illustrative draft only — **not an approved fixture**:

```text
protocolVersion=<REQUIRES DECISION>
mode=<REQUIRES DECISION>
processName=pso2.exe
profileReference=profile-N
serverReference=server-N
```

### 4.3 Required decisions

| Decision | Required owner |
|---|---|
| Include/exclude explicit mode field and its exact value | Security + Core + Launcher |
| Target representation: basename only, PID binding, canonical path, signer, installation binding | Security + Launcher + Core |
| Exact protocol-version value in canonical bytes | Launcher + Core |
| Digest encoding: lowercase hex or unpadded base64url | Backend + Security + Core |
| Opaque reference grammar and maximum lengths | Launcher + Core + Security |
| Approved positive/negative canonical byte fixtures | Joint — all four teams |

No production serializer/hash implementation may be declared interoperable until the approved fixture revision and package hash exist.

---

## 5. Strict permit-verifier requirements

### 5.1 Core-owned invariant checks

Core verifier MUST:

1. bound compact permit and each segment before decoding;
2. require exactly three compact segments;
3. reject malformed base64url, UTF-8, JSON, duplicate security properties, wrong types, and unsupported critical headers;
4. require exact `alg=RS256` and never negotiate/fallback;
5. require bounded non-empty `kid` and resolve exactly one trusted public key;
6. reject missing, unknown, retired, or revoked key IDs without trying arbitrary keys;
7. verify exact signing-input bytes using public verification material only;
8. validate all required claims, exact types, time boundaries, and maximum lifetime;
9. bind permit to challenge, canonical configuration, product/runtime audience, and server-authorized identity/session/install context;
10. compare challenge/configuration bindings with fixed-time comparison where applicable;
11. return typed sanitized decisions only;
12. retain no raw token or decoded claims in public result, log, telemetry, temp, or crash output.

### 5.2 Proposed failure taxonomy

| Internal category | External typed result proposal |
|---|---|
| permit missing | `AuthorizationRequired` |
| malformed/header/signature/claim/binding invalid | `AuthorizationInvalid` |
| expired/not-yet-valid/excess lifetime | `AuthorizationExpired` or policy-approved collapsed code |
| consumed/replaced/replayed authorization | `AuthorizationReplay` |
| key/signer/authority material unavailable | `AuthorizationUnavailable` |
| active session/entitlement revoked | `SessionInactive` |

External failures SHOULD collapse validation detail enough to avoid creating an oracle. Internal aggregate metrics may distinguish allow-listed categories without identity/token content.

### 5.3 Required decisions

| Decision | Required owner |
|---|---|
| Exact JWT `typ` | Security + Backend |
| Trusted issuer and Core audience values | Security + Backend |
| Product/scope names and representation | Security + Backend + Core |
| Required claims, exact names/types, UUID/string policy | Security + Backend |
| `aud` scalar/array policy | Security + Backend |
| NumericDate integer/fractional and inclusive boundary semantics | Security + Backend + Core |
| Permit TTL, maximum lifetime, allowed skew, future-issued tolerance | Security + Backend |
| `kid` grammar/size and public-key format | Security + Backend + Core |
| Rotation overlap, retirement, emergency revocation, signed manifest policy | Security + Backend |
| Whether external errors collapse expired/replay into invalid | Security + Launcher |
| Backend issuance endpoint/envelope/rate/retry/signer-unavailable behavior | Backend + Security + Launcher |

Production private/signing keys MUST remain exclusively in Backend/Security custody and MUST NOT enter this repository, a fixture package, or a release artifact.

---

## 6. Target activation identity and recheck points

### 6.1 Core-owned invariant

Target detection is an activation precondition, never authorization proof. Core proposes these mandatory checks:

1. validate requested target representation before permit issuance/config binding;
2. Launcher detects exact approved target before spawning/activating Core flow;
3. Core validates target after permit verification and before publishing `Starting`;
4. `ProcessModeController` checks immediately before engine start;
5. controller rechecks immediately after engine start and stops engine if target disappeared;
6. runtime monitors the bound target identity and stops on exit.

No target or target replacement at the final pre-side-effect boundary means engine start count zero. Target disappearance after engine start but before `Running` means engine start one, engine stop one, typed `ProcessExited`, and no retained owned state after successful cleanup.

### 6.2 Required decisions

| Decision | Required owner |
|---|---|
| Basename-only versus PID-bound identity | Security + Launcher + Core |
| Canonical path and installation binding | Launcher + Security |
| Executable signature/publisher validation | Security + Launcher |
| Protected-process/access-denied policy | Security + Core |
| PID reuse and process replacement handling | Core + Launcher + Security |

---

## 7. Headless host identity proposal

### 7.1 Core proposal

- executable contract name: proposed `NekoProxyCore.exe`;
- Windows `WinExe`, x64;
- no console, form, tray, notification, message box, or legacy UI composition;
- dependency-complete approved Windows RID bundle;
- bounded current-user Named Pipe as transport isolation only;
- one host instance per approved installation/user scope;
- explicit typed readiness response; process existence or pipe existence is not readiness;
- deterministic cancellation/shutdown and no activation on host boot alone;
- no permit/token/credential in argv, environment, durable file, or process title;
- runtime root canonicalized and restricted to approved installed location;
- production composition fails closed if verifier, key ring, challenge service, or runtime adapter is missing.

### 7.2 Required decisions

| Decision | Required owner |
|---|---|
| Final executable filename/install path | Launcher + Core + Release |
| Pipe and mutex names, installation/user scoping | Launcher + Core + Security |
| RID/runtime dependency policy and signing policy | Release + Security + Core |
| Readiness schema and startup timeout | Launcher + Core |
| Host crash/restart and Launcher ownership semantics | Launcher + Core |

---

## 8. Lifecycle and cleanup ownership

### 8.1 Core-owned proposal

- Before `Starting`, authorization/validation failures own no runtime side effects.
- Once controller start is invoked, Core treats controller state as potentially owned until cleanup succeeds.
- Any failed/partial start MUST invoke controller cleanup.
- If cleanup fails, Core MUST retain ownership state and return a typed sanitized cleanup/stop failure; it MUST NOT report `Stopped` or clear ownership.
- A later explicit stop MUST retry cleanup.
- Monitor failure MUST not clear active ownership before stop succeeds.
- Target exit after `Running` MUST trigger stop through the same serialized lifecycle gate.
- Repeated stop is successful/idempotent only after cleanup has been confirmed.
- Timeout/cancellation MUST not be reported as clean if owned state remains.
- Legacy/native exception details MUST not cross the runtime boundary.

### 8.2 Required decisions

| Decision | Required owner |
|---|---|
| Exact precedence when original start failure and cleanup failure both occur | Core + Security + Launcher |
| Typed code for cleanup-incomplete state (`StopFailed` versus dedicated code) | Core + Launcher + Security |
| Retry count/deadline and process-kill authority for owned helpers | Core + Launcher + Security |
| Host/Launcher behavior when cleanup remains incomplete | Launcher + Core + Security |

---

## 9. Artifact manifest and trusted runtime root

Core proposes that Launcher verify a signed/versioned manifest before launch and that Core revalidate security-critical executable/native inputs before loading where feasible.

Manifest inputs MUST cover:

- production EXE and managed assemblies;
- native DLLs, driver/helper executables, plugins/modes;
- `.deps.json` and runtime configuration;
- security contract/fixture revision and package hash;
- public verification key manifest or immutable key-ring version;
- architecture/RID and product version;
- SHA-256 for every executable/native/code-loading input.

Runtime root MUST be canonicalized, installation-bound, non-user-selectable through untrusted argv, and rejected on missing/mismatched/unlisted executable input. Raw proxy settings and reusable credentials MUST NOT be copied into the repository or contract package.

### Required decisions

| Decision | Required owner |
|---|---|
| Manifest schema/signature format and signing custody | Release + Security |
| Complete code-loading input set | Core + Release + Security |
| Runtime-root installation policy | Launcher + Release + Core |
| Key-ring update/manifest relationship | Security + Backend + Core |
| Short-lived/non-reusable downstream proxy-access architecture | Backend + Proxy Server + Security |

---

## 10. Shared fixture package and revision gate

Required package:

```text
security-contract/
  README.md
  protocol.schema.json
  authority-request.schema.json
  authority-response.schema.json
  canonical-config.txt
  canonical-config.sha256
  signature-positive-vectors.json
  signature-negative-vectors.json
  typed-errors.json
  artifact-manifest.schema.json
```

Every package MUST have a contract revision and SHA-256. Core fixture loaders MUST fail visibly when expected revision/hash is missing or mismatched. Until Security approves dedicated synthetic test material, no private test key will be added. Production key/token/endpoint/customer data is prohibited.

**REQUIRES DECISION — Joint owner:** package location/distribution, revision syntax, package-hash canonicalization, approved synthetic identifiers, dedicated test-key custody, and change-control process.

---

## 11. Owner decision matrix

| Boundary | Core position | Decision owner | Blocks |
|---|---|---|---|
| Challenge primitive | Proposed/implemented internally | Core + Security review | protocol binding |
| Protocol v2 schemas/framing/sizes | Requirements proposed | Launcher + Core + Security | C6/C7 |
| Canonical bytes/hash encoding | Shape proposed, values unfrozen | Backend + Launcher + Security + Core | C2/C3 |
| JWT header/claims/time | Validation invariants proposed | Backend + Security | C3 |
| Public-key format/rotation | Fail-closed behavior proposed | Backend + Security | C3/C7 |
| Backend permit issuance | Required behavior stated | Backend + Security | authorized E2E |
| Target identity | Recheck points proposed | Security + Launcher + Core | C8 |
| Host/pipe/mutex/RID identity | Shape proposed | Launcher + Release + Security + Core | C7/C12 |
| Cleanup failure precedence | Retain ownership/fail closed proposed | Core + Launcher + Security | C5/C7 |
| Continuous authorization | Required, values unresolved | Backend + Security + Core | C11/release |
| Proxy access material | Non-reusable required | Backend + Proxy Server + Security | C11/release |
| Shared fixture package | Required | Joint owner | C2–C6 integration |

---

## 12. Core merge gate

Core-owned S0 work may merge when:

- proposal review comments are resolved or explicitly marked `REQUIRES DECISION` with owner;
- contract-independent cleanup tests are deterministic and full managed suite passes;
- verifier/fixture interfaces contain no guessed production values;
- default runtime and all current production compositions remain fail closed;
- no protocol-v1 production server, allow-all/offline/local-signing path, key, endpoint, or credential is added;
- document and leakage scans pass.

Production verifier/protocol/host work remains blocked until all teams approve the same contract revision/hash and fixtures.

## 13. Cross-repository exit gate

`Core-S0-Producer-01` can be accepted as `DESIGN READY / IMPLEMENTATION PARTIAL` when Launcher, Backend, Security, and Connector can review this proposal without guessing; C5 managed evidence is green; fixture seams fail visibly on revision/hash mismatch; and a sanitized handoff lists every unresolved owner decision.

It MUST NOT be classified runnable, integrated, or production-ready without approved fixtures, Backend-issued permits, production host/artifact, authorized/unauthorized E2E, continuous authorization, and downstream proxy-access evidence.
