# NEKO FAMILY PROXY โ€” IMPLEMENTATION HANDOFF
# Core Telemetry & Observability Implementation Handoff

```text
DOCUMENT:               docs/current/core-telemetry-implementation-handoff.md
STATUS:                 FROZEN (T0 Contract Freeze Completed)
CURRENT_PHASE:          T0_CONTRACT_FREEZE
CURRENT_OWNER:          TEAM_COORDINATION
NEXT_PHASE:             T1_CORE_TELEMETRY_FOUNDATION
NEXT_OWNER:             TEAM_CORE
RECOMMENDED_MODEL:      Gemini 3.7 Flash High
DATE:                   2026-08-17
```

---

## 1. Executive Summary & Objective

This operational handoff document defines the frozen baseline, strict architectural invariants, team boundaries, and execution rules for the implementation of the **Neko Family Proxy Telemetry & Observability Subsystem**.

All architectural contracts, privacy boundaries, and IPC specifications are **FROZEN** under Phase T0.

The primary objective for the incoming implementation team (`TEAM_CORE` for Phase T1) is to build the Core telemetry foundation **without introducing regressions to the proven PSO2 data-plane and without violating the Client Privacy Boundary**.

---

## 2. Current Source Authority Baseline

The current project baseline across all three repositories is verified clean and synchronized:

```text
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| Repository                | Current Git HEAD Commit                    | Branch                                  |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| NekoProxyCore             | bff6501f59749412666a7f71032674086ad68e3f   | feature/neko-auth-lite-v1-core          |
| Neko-Family-Proxy         | 7b55a3cb2d7e494734c197003669062e533f4bee   | feature/neko-auth-lite-v1-launcher-back |
| Neko-Family-Proxy-admin   | ac905ef46186951f5857334e7a922db39e221ab1   | main                                    |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
```

### Verified Runtime & Core Fix Authority
- **Core Fix Authority Commit**: `c3e3fb09ce20de7f05c34bb99cc77f7ebbebc710`
- **Resolved V2Ray Defect**: Replaced invalid standard input invocation (`run -c stdin:`) with direct JSON configuration loading (`run -format=json`).
- **Verified Runtime State**:
  ```text
  AUTH_STATUS                  = AUTHENTICATED
  CORE_STATUS                  = RUNNING
  V2RAY_RUNNING                = YES
  LOCAL_SOCKS_LISTENING        = YES (127.0.0.1:2801)
  SHADOWSOCKS_CONNECTED        = YES
  SHIP_LIST                    = NORMAL
  SHIP_SELECTION               = PASS
  CHARACTER_SELECT             = PASS
  REAL_PSO2_NETWORK_PROXY      = PROVEN & PASS
  ```

---

## 3. Closed Work โ€” DO NOT REOPEN

The following architectural components are **CLOSED** and must NOT be redesigned, refactored, or reopened without new, reproducible regression evidence:

```text
โ DO NOT REDESIGN: Launcher <-> Core Named Pipe Control Protocol (\\.\pipe\NekoProxyCoreControl)
โ DO NOT REDESIGN: Neko Auth Lite Permit Verification and cryptographic validation
โ DO NOT REDESIGN: V2Ray process startup mechanism (run -format=json)
โ DO NOT REDESIGN: Shadowsocks upstream proxy configuration & translation layer
โ DO NOT REDESIGN: Local SOCKS5 listener architecture (127.0.0.1:2801)
โ DO NOT REDESIGN: NetFilter / Redirector ProcessMode packet interception pipeline
โ DO NOT REDESIGN: Real PSO2 network routing logic
```

Telemetry is strictly an **observability extension layer**.

---

## 4. Phase Roadmap & Ownership

```text
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| Phase | Title                                | Primary Team      | Authorized Source Scope                       |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| T0    | Contract Freeze                      | TEAM_COORDINATION | Documentation Only (No source modification)   |
| T1    | Core Telemetry Foundation            | TEAM_CORE         | Core telemetry models, buffer, pipe server    |
| T2    | NetFilter Statistics Instrumentation | TEAM_CORE         | NetFilter/Redirector atomic counters          |
| T3    | Launcher Local Consumer & UI         | TEAM_LAUNCHER     | Launcher Named Pipe client, UI status meters  |
| T4    | Server Monitoring Subsystem          | TEAM_WEB / INFRA  | Japan VPS agent, server metrics API           |
| T5    | Web Session Dashboard                | TEAM_WEB          | Admin Web user session view & controls        |
| T6    | Final Integration & E2E Validation   | TEAM_COORDINATION | Full E2E tests, privacy audit, perf audit     |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
```

---

## 5. Phase T1 Entry Instructions for TEAM_CORE

### 5.1 Authorized Scope for Phase T1:
`TEAM_CORE` is authorized to modify source files **only within `D:\Github\NekoProxyCore`** to implement the telemetry foundation:
1. **Telemetry Event Models**: Standardized message envelope (`schema_version = 1`), `core.health.snapshot`, lifecycle state events, and structured error events.
2. **Telemetry Aggregator & Health Snapshot**: Background periodic timer (500msโ€“1000ms) assembling snapshot payloads from atomic state.
3. **Bounded Ring Buffer**: Thread-safe, non-blocking queue with `DROP_OLDEST` policy and `dropped_telemetry_events` counter.
4. **Named Pipe Publisher**: Dedicated, read-only Windows Named Pipe server at `\\.\pipe\NekoProxyCoreTelemetry` with asynchronous client support.
5. **Failure Isolation**: Guaranteed zero impact on proxy if telemetry pipe creation fails, client disconnects, or buffer overflows.

### 5.2 Explicitly Prohibited in Phase T1:
- โ Do NOT instrument deep per-packet NetFilter hooks yet (reserved for Phase T2).
- โ Do NOT modify Launcher Python code (reserved for Phase T3).
- โ Do NOT modify Admin Web or Backend code (reserved for Phase T4/T5).
- โ Do NOT alter `\\.\pipe\NekoProxyCoreControl` control pipe contracts.
- โ Do NOT transmit raw packet payloads, destination IPs, or secrets.

---

## 6. Build, Test & Runtime Validation Policy

Whenever `TEAM_CORE` makes source modifications to `NekoProxyCore`:

```text
[Step 1] Core Unit & Integration Tests:
         dotnet test D:\Github\NekoProxyCore\Netch.Tests /p:Platform=x64

[Step 2] Canonical Core Release Build:
         dotnet publish D:\Github\NekoProxyCore\Netch -c Release -r win-x64 --self-contained true -o D:\Github\Neko-Family-Proxy\ProxyCore\bin\x64

[Step 3] Build NEW Launcher Debug Executable:
         Execute D:\Github\Neko-Family-Proxy\build_debug_exe.bat

[Step 4] Verify Fresh Executable & Environment:
         - Calculate SHA256 of D:\Github\Neko-Family-Proxy\dist\NekoLauncher-Debug.exe
         - Launch and verify creation of fresh temporary directory (_MEIxxxxxx)

[Step 5] Real Runtime Validation:
         - Launch PSO2 JP game client through Debug Launcher
         - Verify Ship List loading, Ship selection, and Character Selection screen
         - Verify Telemetry Named Pipe is active and publishing snapshots without degradation

[Step 6] Documentation Update:
         - Record test evidence, hashes, and commit authority in handoff document
```

> [!WARNING]
> Running tests against an old Launcher EXE or stale Core DLLs is **STRICTLY FORBIDDEN**.

---

## 7. Mandatory Test Matrix for Telemetry

`TEAM_CORE` must implement and pass the following automated unit/integration tests during T1:

| Test Case | Description | Expected Outcome |
|---|---|---|
| `Telemetry_Pipe_Creates_Successfully` | Core creates `\\.\pipe\NekoProxyCoreTelemetry` on startup. | Named pipe exists in `\\.\pipe\` |
| `Telemetry_Publishes_Health_Snapshot` | Connected client receives valid JSON snapshot within 1s. | Envelope valid, `schema_version = 1` |
| `Telemetry_Client_Disconnect_Safe` | Client disconnects abruptly while Core is streaming. | Core logs debug trace; proxy continues |
| `Telemetry_Slow_Consumer_Non_Blocking` | Client stops reading pipe; buffer fills to capacity. | Oldest dropped; `dropped_telemetry_events > 0`; proxy zero latency |
| `Telemetry_Pipe_Init_Failure_Safe` | Pipe creation forced to fail (e.g. permission/mock). | Core starts proxy normally; logs warning |
| `Telemetry_Envelope_Lenient_Parsing` | Unknown fields added to payload. | Consumer parses without throwing exceptions |
| `Telemetry_Secret_Redaction` | Synthetic error event containing token passed to publisher. | Secret pattern redacted before write |

---

## 8. Privacy & Security Regression Checklist

Before closing any implementation phase:
- [x] Master Brief approved (`docs/current/core-telemetry-monitoring-brief.md`)
- [x] Telemetry Architecture Contract approved (`docs/architecture/core-telemetry-contract.md`)
- [x] Client Privacy Boundary approved (`docs/architecture/client-observability-privacy-boundary.md`)
- [x] Implementation Handoff approved (`docs/current/core-telemetry-implementation-handoff.md`)
- [x] Deep client telemetry (PIDs, flows, DNS, packet capture) verified LOCAL ONLY
- [x] Web / Backend confirmed restricted to Server Aggregate + User Session Authority
- [x] Named Pipe separation confirmed (`Control` vs `Telemetry`)
- [x] Zero secrets present in documentation or telemetry payloads

---

## 9. T0 Exit Gate Evaluation

```text
============================================================
T0 CONTRACT FREEZE EVALUATION MATRIX
============================================================
MASTER_BRIEF_APPROVED                  = YES
TELEMETRY_CONTRACT_APPROVED            = YES
PRIVACY_BOUNDARY_APPROVED              = YES
IMPLEMENTATION_HANDOFF_APPROVED        = YES
TEAM_OWNERSHIP_APPROVED                = YES
PHASE_PLAN_APPROVED                    = YES
BUILD_TEST_POLICY_APPROVED             = YES

TEAM_CORE_CONTRACT_REVIEW              = PASS
TEAM_LAUNCHER_CONTRACT_REVIEW          = PASS
TEAM_WEB_CONTRACT_REVIEW               = PASS

CONTROL_TELEMETRY_SEPARATION           = PASS
TELEMETRY_LOCAL_ONLY                   = YES
TELEMETRY_READ_ONLY                    = YES
TELEMETRY_FAILURE_ISOLATED             = YES
CLIENT_DEEP_TELEMETRY_TO_BACKEND       = NO
WEB_CLIENT_DEEP_VISIBILITY             = NO
SERVER_MONITOR_SOURCE                  = SERVER_SIDE

CORE_SOURCE_CHANGED                    = NO
LAUNCHER_SOURCE_CHANGED                = NO
WEB_SOURCE_CHANGED                     = NO
BUILD_REQUIRED                         = NO
RUNTIME_RETEST_REQUIRED                = NO

SECRETS_WRITTEN_TO_DOCS                = NO
DOCUMENTATION_DIFF_REVIEW              = PASS

T0_EXIT_GATE                           = PASS
HANDOFF_READY                          = YES
NEXT_PHASE                             = T1_CORE_TELEMETRY_FOUNDATION
NEXT_PRIMARY_TEAM                      = TEAM_CORE
RECOMMENDED_MODEL_FOR_NEXT_PHASE       = Gemini 3.7 Flash High
============================================================
```
