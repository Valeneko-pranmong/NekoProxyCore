# NekoProxyCore — Core-S0-Producer-01 Connector Handoff

**Date:** 2026-08-03
**Repository:** `D:\NekoProxyCore`
**Branch:** `feature/neko-headless`
**Baseline revision:** `8d19f36c3c18be52cc36b0102d1496510fdbd30f`
**Commit status:** recorded by the repository commit containing this handoff
**Classification:** `DESIGN READY / IMPLEMENTATION PARTIAL`
**Production integration:** `BLOCKED`

> Sanitized handoff. No production token, permit, private key, credential, endpoint, customer identifier, or raw runtime configuration is included.

## 1. Delivered work

### A — Core-owned S0 proposal

Created `tools/CORE_S0_CONTRACT_PROPOSAL.md`. It proposes and separates ownership for:

- challenge encoding/size/lifetime/replacement/admission/consumption/replay/retry/concurrency;
- Core protocol-v2 validation and allow-listed typed responses;
- canonical field set/order/casing/LF/SHA-256 shape without claiming an approved fixture;
- strict RS256/key/header/claim/time/binding verifier requirements;
- target identity and recheck boundaries;
- headless EXE/pipe/mutex/x64/readiness/single-instance boundaries;
- cleanup ownership/failure precedence;
- artifact manifest/trusted runtime-root requirements;
- merge and cross-repository exit gates.

Unapproved values are marked `REQUIRES DECISION` with owners. No issuer, audience, key, endpoint, TTL, pipe name, credential architecture, or approved schema is invented.

### B — C5 managed cleanup TDD

Changed:

- `NekoProxyCore.Core/HeadlessRuntimeCoordinator.cs`
- `Tests/HeadlessRuntimeTests.cs`

Behavior now proven in managed tests:

1. Coordinator treats controller state as potentially owned before invoking start.
2. Partial start failure invokes controller stop.
3. Failed cleanup returns typed `StopFailed` with `Proxy cleanup failed.`.
4. Failed cleanup retains ownership so explicit stop retries.
5. Monitor failure does not clear ownership before cleanup succeeds.
6. Confirmed cleanup clears ownership; explicit stop does not call engine stop again.
7. Target disappearance after engine start is cleaned through the shared coordinator boundary and returns typed `ProcessExited`.
8. Repeated stop remains deterministic.
9. Process-exit test now waits for published `Stopped`, not only engine-stop completion; stability rerun passed 20/20.

RED evidence observed:

- monitor cleanup expected `StopFailed`, actual `StartFailed`;
- partial start expected stop count 1, actual 0;
- partial cleanup retry expected stop count 2, actual 1;
- stop after confirmed monitor cleanup expected stop count 1, actual 2.

Focused GREEN set: 6/6.

Real native/helper no-orphan proof remains `BLOCKED`; fake/managed evidence is not relabeled as live integration evidence.

### C — Verifier/fixture seams

Created:

- `NekoProxyCore.Core/PermitVerificationContracts.cs`
- `Tests/PermitVerificationContractTests.cs`
- `tools/CORE_S0_NEGATIVE_TEST_MATRIX.md`

Typed contract-independent seams:

- bounded opaque `SensitivePermit`, requiring a positive caller-supplied contract bound;
- redacted permit rendering;
- `IUtcClock` separate from existing monotonic challenge clock;
- canonical serializer interface;
- trusted public-key resolver/key interfaces;
- permit-verifier interface;
- fixture identity containing revision plus raw 32-byte SHA-256;
- fixed-time fixture hash comparison;
- visible sanitized mismatch exception.

The seams contain no production values and are not a JWT verifier. The 25-row negative matrix remains explicitly blocked on approved schema/policy/synthetic vectors.

## 2. Verification evidence

### Managed restore/build

```text
dotnet restore Tests/Tests.csproj
PASS

dotnet build NekoProxyCore.Core/NekoProxyCore.Core.csproj -c Release --no-restore
PASS — 0 warnings, 0 errors

dotnet build NekoProxyCore.Host/NekoProxyCore.Host.csproj -c Release --no-restore
PASS — both net6.0 and net6.0-windows; 0 warnings, 0 errors

dotnet build NekoProxyCore.Windows/NekoProxyCore.Windows.csproj -c Release --no-restore
PASS — 0 warnings, 0 errors
```

### Full managed suite

```text
dotnet test Tests/Tests.csproj -c Release --no-restore
PASS — 64 passed, 0 failed, 0 skipped
```

### Focused/stability evidence

```text
C5 focused lifecycle set
PASS — 6/6

ProcessExitAfterStartupStopsTheEngineAndRuntime repeated run
PASS — 20/20

PermitVerificationContractTests
PASS — 6/6
```

### Legacy Windows target

A direct `dotnet build NekoProxyCore.Legacy/...` failed as expected for this repository/toolchain because it does not initialize the Visual Studio native/resource environment. This result was not hidden and was not used as the gate.

The documented gate was then run with Visual Studio x64 developer environment:

```text
Visual Studio MSBuild 17.14.51
Configuration=Release
Platform=x64
PASS — net6.0 and net6.0-windows outputs produced
```

Existing Netch warnings (10) were reported and not suppressed:

- nullable annotation warnings: 2;
- obsolete API warnings: 2;
- non-nullable initialization warning: 1;
- synchronous wait warnings: 2;
- Windows platform compatibility warnings: 3.

### Diff gate

```text
git diff --check
PASS — no whitespace errors
```

Git emitted LF→CRLF conversion warnings for pre-existing documentation files; these are not diff-check failures.

## 3. Changed files for this round

New:

- `NekoProxyCore.Core/PermitVerificationContracts.cs`
- `Tests/PermitVerificationContractTests.cs`
- `tools/CORE_S0_CONTRACT_PROPOSAL.md`
- `tools/CORE_S0_NEGATIVE_TEST_MATRIX.md`
- `tools/CORE_S0_PRODUCER_01_HANDOFF.md`

Modified for C5:

- `NekoProxyCore.Core/HeadlessRuntimeCoordinator.cs`
- `Tests/HeadlessRuntimeTests.cs`

Pre-existing C1 documentation modifications remain uncommitted in the same worktree and are not reverted.

## 3.1 Independent review closure

Independent review initially reported four blockers. Current resolution:

1. restart while cleanup ownership remains — **FIXED**, start now blocks when `_activeConfiguration` remains owned; regression attempts duplicate start before cleanup retry;
2. non-cooperative timed-out startup — **FIXED**, pending start task remains owned, stop is not raced against it, new starts stay blocked, and serialized cleanup runs after the underlying task completes;
3. typed runtime/monitor exception leakage — **FIXED**, coordinator maps typed codes to allow-listed messages and sentinel tests cover start and monitor seams;
4. public fixture revision exposure — **FIXED**, raw revision is internal-only and reflection regression confirms no public `Revision` property.

Focused review regression set: `6/6` passed. Full managed suite after fixes: `64/64` passed.

## 4. Unresolved decisions by owner

### Core + Security

- exact challenge admission/consumption point;
- disconnect behavior before/after admission;
- cleanup-failure precedence and external code;
- protected-process/access-denied behavior.

### Launcher + Core + Security

- exact protocol-v2 schema/framing/casing/unknown/duplicate policy;
- frame/permit/segment/correlation bounds;
- pipe name/ACL and timeout values;
- target PID/path/signature/installation binding;
- error compatibility policy.

### Backend + Security

- JWT `typ`, issuer, audience, product, scope, required claims/types;
- NumericDate boundaries, TTL, maximum lifetime, skew;
- `kid`, public-key format, rotation/revocation;
- permit issuance schema/endpoint/retry/rate/signer-unavailable behavior.

### Joint all teams

- approved versioned fixture package, revision syntax, package-hash process;
- canonical bytes and digest encoding;
- dedicated synthetic test key/vectors;
- change control and cross-repository merge gates.

### Backend + Proxy Server + Security

- short-lived/non-reusable downstream proxy access architecture;
- renewal/revocation/grace policy.

## 5. Gate classification

| Boundary | Status | Evidence |
|---|---|---|
| Core S0 proposal | DESIGN READY | sanitized proposal and owner matrix |
| C5 managed cleanup | PASS | RED/GREEN regressions + 60/60 full suite |
| Process-exit test stability | PASS (managed) | 20/20 focused rerun |
| Verifier/fixture seams | PARTIAL | seam tests pass; no crypto verifier |
| Strict RS256 verifier | BLOCKED | contract/key/vectors not frozen |
| Protocol v2/production host | BLOCKED | schema/identity/timeouts not frozen |
| Legacy Windows build | PASS WITH WARNINGS | VS MSBuild 17.14.51; 10 existing Netch warnings |
| Native/helper no-orphan | BLOCKED | no authorized live environment |
| Production artifact/manifest | BLOCKED | no approved host/release package |
| Authorized Launcher→Core E2E | BLOCKED | Backend permit/contract unavailable |
| Gameplay/Shadowsocks evidence | BLOCKED | production authorization and live coordination unavailable |

## 6. Merge gate

This round is suitable for Connector/Security review as `DESIGN READY / IMPLEMENTATION PARTIAL` with the independent review blockers above resolved. It is not production-ready.

Do not start C2–C4 or C6–C7 production behavior from proposal examples. Wait for one approved contract revision/hash and fixture package.

## 7. Sanitization declaration

Reviewed deliverables intentionally contain no production token, permit, private/signing key, proxy credential, endpoint, customer/account/session/license/installation identifier, or raw runtime configuration. Any example label is synthetic/opaque and not production material.
