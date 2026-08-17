# NEKO FAMILY PROXY โ€” MASTER BRIEF
# Core Telemetry, NetFilter Statistics & Server Monitoring Brief

```text
DOCUMENT:               docs/current/core-telemetry-monitoring-brief.md
STATUS:                 FROZEN (T0 Contract Freeze)
CONTRACT VERSION:       1
OWNER:                  TEAM_COORDINATION
IMPLEMENTATION OWNER:   TEAM_CORE
CONSUMERS:              TEAM_LAUNCHER, TEAM_WEB
DATE:                   2026-08-17
```

---

## 1. Purpose & Vision

The objective of this architecture is to establish a robust, fail-safe observability framework for NekoProxyCore and the Neko Family Proxy ecosystem:

1. **Core Internal Telemetry**: Core produces structured, lightweight runtime metrics, lifecycle events, and health snapshots.
2. **NetFilter / Redirector Statistics**: Real-time traffic, redirect, and network counter accounting on the local machine.
3. **Launcher Local Observability**: Launcher consumes local Core telemetry to present rich, real-time status and diagnostics to the user.
4. **Server Aggregate Monitoring**: Independent Japan VPS monitoring agent collecting aggregate server health, bandwidth, and service metrics.
5. **Admin Web Management**: Web interface limited strictly to Server Aggregate Monitoring and User Session Management.
6. **Client Privacy Boundary**: Strict, inviolable barrier ensuring deep client machine internals NEVER leave the user's computer.

### Primary Directives
- **NO PROXY REGRESSION**: The existing, validated PSO2 proxy data-plane MUST NOT be redesigned, slowed down, or compromised.
- **NO SURVEILLANCE**: The Admin Web and Backend MUST NOT receive detailed machine telemetry, packet data, process details, or destination history from the client.
- **FAIL-SAFE ISOLATION**: Telemetry failures or slow consumers MUST NEVER interrupt proxy routing or game connectivity.

---

## 2. Hard Architecture Invariants

### 2.1 Client Privacy Boundary (Collection Boundary)
The project-level privacy rule is:
```text
THE ADMIN WEB MAY KNOW WHO IS ONLINE,
BUT MUST NOT KNOW WHAT IS HAPPENING INSIDE THE USER'S MACHINE.
```

Equivalent system boundary:
```text
WEB = SERVER AGGREGATE + USER SESSION AUTHORITY
WEB != CLIENT OBSERVABILITY
```

TEAM_WEB and Backend MUST NOT receive:
- Game PID, Core PID, v2ray PID
- Local SOCKS listening port
- Process list or command lines
- DNS query history or contents
- Destination IP / hostname history
- TCP connection lists or UDP flow lists
- Per-flow, per-destination, or per-process RX/TX breakdown
- Raw packet payload, packet headers, or packet capture
- NetFilter raw event stream or Redirector raw event stream
- Core debug logs, trace logs, or internal memory dumps
- Client network diagnostics or adapter details
- Shadowsocks passwords, credentials, JWTs, permits, or signing keys

> [!IMPORTANT]
> This is a **COLLECTION BOUNDARY**, not merely a UI display filter. Detailed client telemetry must not be sent to the backend and hidden on the web; it must never leave the client machine.

### 2.2 Web Visibility Scope
TEAM_WEB is strictly permitted to receive and display:

1. **Server Aggregate Information (Originating from Server)**:
   - Server Online / Offline status
   - Server Ping / Round-Trip Time
   - Packet Loss percentage
   - Aggregate RX / TX bytes total
   - Aggregate RX / TX bandwidth (bps)
   - Server uptime
   - Proxy / Shadowsocks service health
   - Active user count
   - System load (CPU / RAM / Disk)

2. **User Session Authority (Originating from Auth/Session System)**:
   - `user_id`
   - Session reference / `session_id`
   - Online / Offline status
   - Active / Inactive state
   - Login / session start timestamp
   - Last heartbeat timestamp
   - Entitlement / subscription status
   - Administrative session actions: Revoke Session, Kick Session, Entitlement administration

---

## 3. Current Proven Runtime Baseline

The real PSO2 data-plane has been fully validated in production and end-to-end testing:

```text
pso2.exe
    โ“
netfilter2 / Redirector
    โ“
local SOCKS5 (127.0.0.1:2801)
    โ“
v2ray-sn.exe
    โ“
Shadowsocks (AES-128-GCM / 2022)
    โ“
Japan Upstream VPS
    โ“
PSO2 JP Game Servers
```

### Verified Runtime Evidence
- Core Fix Authority: Commit `c3e3fb09ce20de7f05c34bb99cc77f7ebbebc710`
- Resolved Defect: V2Ray invocation updated from `run -c stdin:` to `run -format=json`
- Proven Runtime State:
  ```text
  AUTH_STATUS                  = AUTHENTICATED
  CORE_STATUS                  = RUNNING
  V2RAY_RUNNING                = YES
  LOCAL_SOCKS_LISTENING        = YES
  SHADOWSOCKS_CONNECTED        = YES
  SHIP_LIST                    = NORMAL
  SHIP_SELECTION               = PASS
  CHARACTER_SELECT             = PASS
  REAL_PSO2_PROXY_PROVEN       = YES
  ```

The V2Ray and Shadowsocks proxy path is **CLOSED** and must not be reopened or refactored. Telemetry is an observability extension layer built around the working core.

---

## 4. Target Observability Architecture & Layers

Observability is divided into three distinct architectural layers:

```text
+-------------------------------------------------------------------------+
| Layer 1: Core Internal Telemetry (User Machine - Local Only)            |
| - Counters, Health Snapshot, Lifecycle Events                           |
| - Bounded Ring Buffer -> \\.\pipe\NekoProxyCoreTelemetry               |
+-------------------------------------------------------------------------+
                                    โ”
                                    โ–ผ (Named Pipe IPC)
+-------------------------------------------------------------------------+
| Layer 2: Launcher Local Status (User Machine - Local Only)              |
| - Local UI: Ping, Speed, RX/TX, Uptime, Core Health                    |
| - Local Diagnostics (No automated forwarding to Backend)                |
+-------------------------------------------------------------------------+

+-------------------------------------------------------------------------+
| Layer 3: Server Monitoring & Session Authority (Backend / Admin Web)    |
| - Japan VPS Monitoring Agent -> Aggregate Metrics API -> Admin Web      |
| - Auth / Session Authority -> Heartbeat Freshness -> Session Dashboard  |
+-------------------------------------------------------------------------+
```

### Layer 1 โ€” Core Internal Telemetry
- **Owner**: TEAM_CORE
- **Location**: User machine exclusively
- **Components**: Event Collector, Stats Aggregator, Health Snapshot Provider, Bounded Buffer, Named Pipe Publisher (`\\.\pipe\NekoProxyCoreTelemetry`).
- **Data Scope**: Core state, v2ray state, NetFilter counters, Redirector counters, TCP/UDP/DNS aggregate counters, RX/TX bytes, dropped telemetry events.

### Layer 2 โ€” Launcher Local Status
- **Owner**: TEAM_LAUNCHER
- **Location**: User machine exclusively
- **Source**: Local Core Telemetry Pipe (`\\.\pipe\NekoProxyCoreTelemetry`)
- **User Display**: Real-time connected status, latency, download/upload rate, total session transfer, uptime, proxy health indicators.
- **Rule**: Launcher consumes telemetry for local presentation only. It MUST NOT upload deep client telemetry to Backend or third parties.

### Layer 3 โ€” Server Aggregate & User Session
- **Owner**: TEAM_WEB / Backend Infrastructure
- **Location**: Server / Cloud / Admin Web
- **Source**: Server-side monitoring agent (Japan VPS) and authentication session database.
- **Scope**: Fleet-wide aggregate statistics, server health, active session counts, and session revocation.

---

## 5. Core Telemetry IPC Architecture

To guarantee isolation between control and observability, two separate Windows Named Pipes are defined:

```text
Control Channel:     \\.\pipe\NekoProxyCoreControl      (Bidirectional, Privileged, Critical)
Telemetry Channel:   \\.\pipe\NekoProxyCoreTelemetry    (Unidirectional Publisher, Local-Only, Fail-Safe)
```

### IPC Separation Rules
1. **Unidirectional & Read-Only**: The Telemetry Pipe is an observation-only stream. Consumers read snapshots and events; they cannot write commands.
2. **Prohibited on Telemetry Channel**: Start/stop commands, permit delivery, JWTs, configuration changes, routing updates, or process commands.
3. **Non-Blocking Operation**: Writing to the telemetry pipe must use asynchronous/non-blocking I/O with bounded buffers.
4. **Independent Lifecycle**: If the telemetry pipe fails to create or crashes, Core runtime continues proxying uninterrupted.

---

## 6. Failure Isolation Invariant

```text
TELEMETRY_FAILURE MUST NOT CAUSE PROXY_FAILURE
```

| Scenario | System Behavior |
|---|---|
| **Launcher Consumer Disconnects** | Core continues proxying; PSO2 unaffected; telemetry publisher drops/buffers events per policy. |
| **Telemetry Pipe Fails to Initialize** | Core logs warning; proxy engine starts normally; game traffic continues. |
| **Slow Telemetry Consumer** | Telemetry buffer fills; oldest/newest telemetry dropped; drop counter incremented; zero latency impact on proxy path. |
| **Telemetry JSON Serialization Error** | Error swallowed/recorded; packet forwarding thread unaffected. |

---

## 7. Hot-Path Performance Architecture

NetFilter and the Redirector process time-critical network packets. Synchronous telemetry overhead on the hot path is strictly prohibited.

```text
[ Hot Path: Packet Event ]
           โ”
           โ–ผ
[ Atomic Counter Increment (Interlocked.Increment / Add) ]  <-- Minimal CPU overhead (sub-microsecond)
           โ”
           โ”  (Asynchronous decoupling)
           โ–ผ
[ Background Periodic Aggregator (e.g. 500ms - 1000ms timer) ]
           โ”
           โ–ผ
[ Snapshot Generation & Bounded Buffer ]
           โ”
           โ–ผ
[ Named Pipe Non-Blocking Async Write ]
```

### Performance Invariants
- NO synchronous disk I/O on packet handling threads.
- NO synchronous Named Pipe writes on packet handling threads.
- NO JSON serialization on packet handling threads.
- NO heap allocations per packet for telemetry.
- NO packet payload inspection or capture.

---

## 8. Server Monitoring Architecture (Server-Side Origin)

Server monitoring MUST originate from server-side infrastructure:

```text
Japan VPS
   โ”
   โ–ผ
Monitoring Agent (Node/Go/Python daemon or system metrics exporter)
   โ”
   โ–ผ
Metrics / Aggregator API
   โ”
   โ–ผ
Backend / Supabase
   โ”
   โ–ผ
Admin Web Dashboard
```

> [!WARNING]
> Collecting telemetry from hundreds of game clients and aggregating it on the backend to deduce server load is **FORBIDDEN**. Server metrics must come directly from the VPS.

---

## 9. User Session Model & Administration

The concept of an "Active User" is derived from session freshness:

```text
ACTIVE USER = Valid Active Session + Heartbeat Received within Freshness Window
```

### Minimal Client Heartbeat
```json
{
  "session_id": "019163ab-8f92-7411-b0e2-7634891b0123",
  "client_version": "1.0.0-debug",
  "heartbeat_utc": "2026-08-17T02:30:00.000Z"
}
```

### Administrative Actions Allowed on Admin Web
- `REVOKE_SESSION`: Invalidate the session token in the database.
- `KICK_SESSION`: Force token expiration causing Launcher to disconnect on next auth refresh.
- `UPDATE_ENTITLEMENT`: Grant/revoke user service tiers.

### Prohibited Administrative Actions
Web MUST NOT issue machine-level remote execution commands (e.g. `KILL_PROCESS`, `DUMP_MEMORY`, `CAPTURE_PACKETS`, `RUN_COMMAND`).

---

## 10. Team Ownership & Responsibilities

```text
+---------------------+-------------------------------------------------------------+
| Team                | Direct Responsibilities                                     |
+---------------------+-------------------------------------------------------------+
| TEAM_CORE           | - Core telemetry event model & counters                     |
|                     | - Stats aggregator & health snapshot generation             |
|                     | - Named Pipe publisher (\\.\pipe\NekoProxyCoreTelemetry)   |
|                     | - NetFilter & Redirector atomic counter instrumentation     |
|                     | - Hot-path safety, performance budgets & failure isolation  |
+---------------------+-------------------------------------------------------------+
| TEAM_LAUNCHER       | - Local telemetry Named Pipe consumer                       |
|                     | - Local UI metrics display (Ping, Speed, RX/TX, Health)     |
|                     | - Local diagnostics & graceful degradation handling         |
|                     | - Ensuring no deep client metrics leak to Backend           |
+---------------------+-------------------------------------------------------------+
| TEAM_WEB            | - Server aggregate monitoring dashboard                     |
|                     | - VPS monitoring agent integration & metrics API            |
|                     | - User session management & administrative controls         |
|                     | - Enforcing strict data minimization on all web endpoints   |
+---------------------+-------------------------------------------------------------+
| TEAM_COORDINATION   | - Architecture governance & telemetry contract freeze       |
|                     | - Privacy boundary compliance & security audits             |
|                     | - Cross-team handoff authority & release gates              |
|                     | - Master documentation authority                            |
+---------------------+-------------------------------------------------------------+
```

---

## 11. Implementation Roadmap

```text
[ T0: Contract Freeze ]  <-- CURRENT PHASE
       โ”
       โ–ผ
[ T1: Core Telemetry Foundation ]       (TEAM_CORE: Base models, buffer, publisher, pipe)
       โ”
       โ–ผ
[ T2: NetFilter Statistics ]            (TEAM_CORE: Atomic counters, redirector metrics)
       โ”
       โ–ผ
[ T3: Launcher Local Consumer ]         (TEAM_LAUNCHER: Pipe client, UI meters, diagnostics)
       โ”
       โ–ผ
[ T4: Server Monitoring ]               (TEAM_WEB/INFRA: VPS agent, aggregator API)
       โ”
       โ–ผ
[ T5: Web Session & Dashboard ]         (TEAM_WEB: Admin UI, session management)
       โ”
       โ–ผ
[ T6: Final Integration & Release ]     (TEAM_COORDINATION: E2E, performance, privacy gate)
```

---

## 12. Build & Runtime Validation Policy

To prevent testing drift and stale binary execution:

1. **Core Source Modification**:
   - Run Core unit and Windows tests.
   - Perform canonical Core build (`dotnet publish -c Release`).
   - Stage fresh Core binaries into Launcher project directory.
   - Build **NEW** Launcher Debug EXE (`build_debug_exe.bat` / PyInstaller).
   - Verify executable hash and confirm fresh `_MEIxxxxxx` temporary directory creation.
   - Perform real PSO2 runtime test (Ship select & character screen).
   - Update handoff documentation.
   - *Testing against an old Launcher EXE after Core changes is strictly FORBIDDEN.*

2. **Launcher Source Modification**:
   - Run pytest suite.
   - Build **NEW** Launcher Debug EXE.
   - Validate UI against mock/running Core.

3. **Web / Server Modification**:
   - Run web tests and staging validation.
   - Core and Launcher rebuilds NOT required unless shared client API contract changed.

4. **Documentation-Only Modification (Phase T0)**:
   - `BUILD_REQUIRED = NO`
   - `RUNTIME_RETEST_REQUIRED = NO`

---

## 13. Security & Non-Goals

### Prohibited Data in Telemetry & Logs
- Shadowsocks passwords & preshared keys
- JWT access tokens & refresh tokens
- Launch permits & cryptographic signatures
- Supabase service role keys & database connection strings
- Raw game packet payloads or decrypted streams

### Non-Goals for Version 1
- Remote packet capture or PCAP dump capabilities
- Remote telemetry streaming to cloud dashboards
- Web-based per-client traffic inspection
- Full destination URL/domain history tracking
- Remote arbitrary command execution on client PCs
- Redesigning or replacing the existing NetFilter/Redirector architecture
