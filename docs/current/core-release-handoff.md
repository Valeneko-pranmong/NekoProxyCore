# NekoProxyCore — Core Release & Authority Handoff

```text
DOCUMENT:                               docs/current/core-release-handoff.md
STATUS:                                 PRE-T10 REPOSITORY HYGIENE
LATEST_SOURCE_AUTHORITY:                f269627351fc6a2c13b07c90f0e43ff69d17f058
LATEST_SOURCE_BRANCH:                   feature/neko-auth-lite-v1-core
HISTORICAL_S0_RELEASE_BASELINE:         b3c9d0851cff74691500c431c0da1ec30c21927a
V2RAY_LIVE_DATA_PLANE_FIX:              c3e3fb09ce20de7f05c34bb99cc77f7ebbebc710
DEPLOYED_OR_PACKAGED_ARTIFACT_AUTHORITY: AMBIGUOUS / NOT YET REFROZEN
CONTRACT:                               NEKO-AUTH-LITE (lite-v1) + Local Telemetry Named Pipe
DATE:                                   2026-08-18
```

---

## 1. Authority Reconciliation & Provenance

To eliminate cross-repository ambiguity, Core development and release authority are strictly distinguished:

| Authority Level | Branch / Commit | Classification & Role |
| :--- | :--- | :--- |
| **`LATEST_SOURCE_AUTHORITY`** | `feature/neko-auth-lite-v1-core`<br>(`f269627351fc6a2c13b07c90f0e43ff69d17f058`) | **Active Core Source Line** — Contains Lite v1 challenge verification, V2Ray stdin fix, and local telemetry engine (Phases T1–T2B). |
| **`HISTORICAL_S0_BASELINE`** | `origin/feature/neko-headless`<br>(`b3c9d0851cff74691500c431c0da1ec30c21927a`) | **Historical Release Baseline** — Previous S0 permit verification line. |
| **`V2RAY_PROVEN_DATA_PLANE_FIX`**| `c3e3fb09ce20de7f05c34bb99cc77f7ebbebc710` | **Verified Data Plane Fix** — Invocation fix (`run -format=json`) proven with real PSO2 game client traffic. |
| **`DEPLOYED_PACKAGED_ARTIFACT`**| `AMBIGUOUS / NOT YET REFROZEN` | Hosted Lite cutover has not been performed; packaged binary provenance will be refrozen after cross-component E2E. |

> [!IMPORTANT]
> **Source vs. Production Artifact Distinction:**
> Commit `f269627` is the **LATEST CURRENT SOURCE**, but is **NOT automatically the production release binary**.
> Phase T10 Commercial Launcher UI/UX is authorized to consume the existing Launcher-facing telemetry contract without declaring a new Core production binary cutover.

---

## 2. Current Contracts & Runtime Behavior

### NEKO-AUTH-LITE Contract (`lite-v1`):
- Contract Authority: [`neko-auth-lite-core-contract.md`](neko-auth-lite-core-contract.md)
- Production permit signing authority: `neko-prod-key-2`, RSA 3072-bit, SPKI SHA-256:
  ```text
  4a0ef40a483c6a4f294724ea62d0ae55357176e196c9747defec06769a0d0801
  ```
- Core production start path remains strictly fail-closed:
  ```text
  HeadlessControlServer START
    -> HeadlessRuntimeCoordinator.StartAsync
    -> ChallengePermitStartAuthorizer
    -> StrictLaunchPermitVerifier
    -> ProcessModeController
    -> NetchProcessModeEngine
  ```
- Replay protection: Process-local atomic JTI tracking.

### Data Plane & V2Ray Invocation:
- Runtime Authority: [`v2ray-runtime-fix-handoff.md`](v2ray-runtime-fix-handoff.md)
- Data plane fix (`run -format=json`) prevents Windows `stdin:` path syntax errors and is verified with real PSO2 ship connectivity.

### Telemetry Architecture:
- Architecture Contract: [`../architecture/core-telemetry-contract.md`](../architecture/core-telemetry-contract.md)
- Named Pipe: `\\.\pipe\NekoProxyCoreTelemetry`
- Strict privacy: Zero customer identifiers, zero token logging, bounded 1-second aggregator snapshots.

---

## 3. Verification Commands

Canonical test command:
```powershell
dotnet test Tests/Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo
```

Source validation status:
- Core tests: `201 passed, 0 failed, 0 skipped`
- Data plane status: `REAL_PSO2_NETWORK_PROXY_PROVEN = YES`
- Security status: `private signing key in Core = absent; service-role in Core = absent`
