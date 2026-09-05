# NEKO-AUTH-LITE Core Contract

**Contract:** `NEKO-AUTH-LITE`
**Revision:** `lite-v1`
**Scope:** Core launch authorization only. S0 history remains under `docs/archive/security-s0/`.

## Enforcement

Core is final launch boundary:

```text
NO VALID BACKEND-SIGNED PERMIT
=
NO PROXY ENGINE START
```

Production path:

```text
NekoProxyCore.Host.Program
  -> HeadlessControlServer START
  -> HeadlessRuntimeCoordinator.StartAsync
  -> ChallengePermitStartAuthorizer
  -> StrictLaunchPermitVerifier
  -> ProcessModeController
  -> NetchProcessModeEngine
```

Verifier failure returns typed authorization failure before process precondition, runtime status `Starting`, engine, network, packet hook, or driver side effects.

Direct Core execution waits on control authorization. No cached permit, offline fallback, previous authorization, local authorized flag, or reusable permit cache exists.

## Lite permit

Compact JWT. Required header:

```json
{"alg":"RS256","typ":"neko-launch+jwt","kid":"neko-prod-key-2"}
```

Required claims:

```text
iss = neko-backend
aud = neko-proxy-core
sub = non-empty bounded ASCII subject
product = neko-family-proxy
scope = proxy:start
challenge = current Core challenge
jti = non-empty bounded ASCII ID
iat = integer date
exp = iat + 30
```

`nbf` optional. If present, integer and equal to `iat`.

Core pins exact KID `neko-prod-key-2`. Bundled authority is RSA 3072-bit with SPKI SHA-256:

```text
4a0ef40a483c6a4f294724ea62d0ae55357176e196c9747defec06769a0d0801
```

RSA verification uses platform `.NET RSA.VerifyData` with SHA-256 and PKCS#1 v1.5 padding. No manual RSA primitive. No fallback key. Private signing key never appears in Core.

Lite lifetime uses trusted UTC clock and short skew policy. Compact structure, size, ASCII, duplicate fields, unknown fields, exact algorithm/type/KID, signature, claims, challenge, expiry, and replay all fail closed.

## Challenge

`CoreChallengeService` creates 32 random bytes with `RandomNumberGenerator.Fill`, encodes base64url without padding, and returns 43 characters. One outstanding challenge exists. New issue replaces old pending challenge. START admission consumes challenge before verifier dispatch. Failed or successful admitted attempt consumes it. Retry requires fresh challenge. Monotonic expiry is at most 30 seconds. Lock protects replacement and one-use concurrency.

## Replay

`InMemoryPermitReplayStore` gives process-local atomic JTI admission through `ConcurrentDictionary.TryAdd`. First use succeeds. Same JTI rejects as `AuthorizationReplay`. Concurrent same-JTI attempts admit at most one. No global DB replay ledger exists in Core.

## Removed S0 bindings

Lite verification does not require or compare:

```text
sid
iid
lid
cfg
target_pid
mode
configurationDigest
canonical configuration SHA-256
target PID cryptographic binding
mode cryptographic binding
```

S0 serializer and tests remain historical reference only. Runtime configuration remains business data: `processName`, `targetPid`, `profileReference`, `serverReference`, and `ProcessMode` still drive normal runtime validation and engine behavior after authorization.

## Hardening retained

Bounded newline-delimited control frames, strict malformed JSON handling, duplicate-field rejection, correlation IDs, permit redaction, current-user pipe isolation, exact target process recheck, safe diagnostics, protected runtime settings, and lifecycle cleanup remain in place.

## Threat model and non-goals

Lite blocks direct Core execution, copied Core use, fake START commands, fake Launcher authorization, fake JWTs, old permits against fresh challenges, wrong challenge, expired permits, wrong signatures, unknown KIDs, and casual bypass attempts.

Out of scope: machine-code patching, advanced reverse engineering, debugger-assisted modification, kernel/admin/root compromise, advanced injection, sophisticated memory patching, commercial DRM, and anti-debugging arms races.

Lite does not authorize embedding reusable server secrets in executable. No production artifact, deployment, or hosted cutover is performed by this source migration.

S0 remains current production authority until coordinated cross-component E2E and cutover approval.

Cross-team authority:

```text
LAUNCHER_BACKEND_LITE_AUTHORITY = 3f54288012aaf8c2d459d25faccd18d373ab0724
```

Status:

```text
HOSTED LITE CUTOVER = NOT PERFORMED
LITE PRODUCTION ARTIFACT = NOT RELEASED
CROSS-COMPONENT LITE E2E = NOT YET EXECUTED
```

Next phase: Launcher + Backend Lite plus Core Lite, automated cross-component E2E, manual E2E, then production cutover decision.
