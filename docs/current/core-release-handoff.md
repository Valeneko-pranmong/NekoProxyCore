# NekoProxyCore โ€” NEKO-AUTH-LITE Core Release Handoff

- **Contract:** `NEKO-AUTH-LITE`
- **Revision:** `lite-v1`
- **Scope:** Core source/test preparation. No deployment or production artifact.
- **Historical S0 handoff:** [`../archive/security-s0/core-release-handoff-s0.md`](../archive/security-s0/core-release-handoff-s0.md)
- **Lite contract:** [`neko-auth-lite-core-contract.md`](neko-auth-lite-core-contract.md)

## Current status

Core Lite verifier and production composition use `NEKO-AUTH-LITE/lite-v1`.
Production permit authority remains exact `neko-prod-key-2`, RSA 3072-bit, SPKI SHA-256:

```text
4a0ef40a483c6a4f294724ea62d0ae55357176e196c9747defec06769a0d0801
```

Core production start path remains fail closed:

```text
HeadlessControlServer START
  -> HeadlessRuntimeCoordinator.StartAsync
  -> ChallengePermitStartAuthorizer
  -> StrictLaunchPermitVerifier
  -> ProcessModeController
  -> NetchProcessModeEngine
```

Verifier accepts only valid backend-signed RS256 launch permit bound to fresh Core challenge. JTI replay is process-local and atomic. Authorization occurs before runtime precondition, `Starting`, engine, network, packet hook, or driver side effects.

Runtime business configuration remains intact. S0 JWT bindings to `sid`, `iid`, `lid`, `cfg`, `target_pid`, `mode`, canonical configuration digest, target PID, and mode are removed from Lite verification.

## Verification

Canonical command:

```text
dotnet test Tests/Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo
```

Current source validation in this migration:

```text
Core tests: 201 passed, 0 failed, 0 skipped
Known build warning: Tests/Global.cs SYSLIB0021 SHA1CryptoServiceProvider obsolete
```

Release/x64 build and full publish/security process smoke remain required gates before integration approval. Direct Core no-permit behavior must remain runtime start count `0`.

## Data plane status

```text
V2RAY_FIX = CLOSED
CORE_DATA_PLANE = PASS
REAL_PSO2_NETWORK_PROXY_PROVEN = YES
CORE_FIX_COMMIT = 3954b0fa03c5188bfdd7faea7b0fe30ba4d9fe89
```

Authoritative data-plane handoff: [`v2ray-runtime-fix-handoff.md`](v2ray-runtime-fix-handoff.md)

## Security status

```text
private signing key in Core = absent
service-role key in Core = absent
permit logging = redacted / absent
cached authorization = absent
production bypass path = none found
```

## Cross-team dependency

```text
LAUNCHER_BACKEND_LITE_AUTHORITY = 3f54288012aaf8c2d459d25faccd18d373ab0724
```

## Production status

```text
HOSTED LITE CUTOVER = NOT PERFORMED
LITE PRODUCTION ARTIFACT = NOT RELEASED
CROSS-COMPONENT LITE E2E = NOT YET EXECUTED
V2RAY DATA PLANE DEFECT = CLOSED / PROVEN
```

S0 remains current production authority. Next phase: coordinated Launcher + Backend Lite and Core Lite cross-component automated E2E, manual E2E, then cutover decision.

Historical S0 documents remain unchanged under `docs/archive/security-s0/`.
