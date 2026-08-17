# NEKO FAMILY PROXY โ€” PRIVACY ARCHITECTURE
# Client Observability Privacy Boundary

```text
DOCUMENT:               docs/architecture/client-observability-privacy-boundary.md
STATUS:                 FROZEN (T0 Contract Freeze)
CLASSIFICATION:         HARD ARCHITECTURE INVARIANT
OWNER:                  TEAM_COORDINATION
REVIEWERS:              TEAM_CORE, TEAM_LAUNCHER, TEAM_WEB
DATE:                   2026-08-17
```

---

## 1. Executive Summary & Core Invariant

This document establishes the inviolable **Privacy Boundary** between the local client ecosystem (Core & Launcher) and the remote cloud/web infrastructure (Backend, Supabase, and Admin Web).

The foundational principle of the Neko Family Proxy architecture is:

```text
THE ADMIN WEB MAY KNOW WHO IS ONLINE,
BUT MUST NOT KNOW WHAT IS HAPPENING INSIDE THE USER'S MACHINE.
```

### System Identity Boundary:
```text
WEB SCOPE     = SERVER AGGREGATE MONITORING + USER SESSION AUTHORITY
CLIENT SCOPE  = DEEP LOCAL OBSERVABILITY & DIAGNOSTICS (LOCAL ONLY)
```

```text
WEB != CLIENT OBSERVABILITY
```

---

## 2. Three-Layer Observability Architecture

Observability data is classified into three isolated layers with strict boundaries:

```text
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| LAYER 1: Core Internal Telemetry                                            |
| Location: Local Machine (Core Memory / Pipe)                                |
| Scope: NetFilter stats, Redirector counters, PID, TCP/UDP counters          |
| Boundary: NEVER LEAVES LOCAL PROCESS IPC                                    |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
                                       โ”
                                       โ–ผ (Local Pipe IPC Only)
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| LAYER 2: Launcher Local Status & Diagnostics                                |
| Location: Local Machine (Launcher UI Memory)                               |
| Scope: Live ping, transfer counters, speed meters, local health indicators  |
| Boundary: DISPLAYED TO LOCAL USER ONLY - NEVER SENT TO BACKEND               |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+

+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
| LAYER 3: Server Aggregate & User Session Authority                          |
| Location: Server Infrastructure & Admin Web                                 |
| Scope: Japan VPS aggregate metrics + Auth/Session database records          |
| Boundary: SERVER-SIDE ORIGIN ONLY - NO CLIENT SURVEILLANCE                   |
+โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+
```

---

## 3. Allowed Web & Backend Information

The Admin Web and Backend APIs are strictly authorized to receive and store only the following data categories:

### 3.1 Server Aggregate Metrics (Originating from Server-Side Agent)
- `server_online` (Boolean)
- `server_ping_ms` (Gateway round-trip time)
- `packet_loss_percent` (Aggregate ping loss)
- `server_rx_bytes_total` / `server_tx_bytes_total` (VPS interface counters)
- `server_rx_bps` / `server_tx_bps` (Aggregate interface bandwidth)
- `server_uptime_seconds`
- `proxy_service_status` (Systemd/daemon health)
- `shadowsocks_service_status` (Upstream port health)
- `active_user_count` (Calculated from active sessions)
- Server hardware metrics: CPU load %, RAM usage %, Disk usage % (Optional server health)

### 3.2 User Session State (Originating from Auth/Session System)
- `user_id` (UUID / Account ID)
- `session_id` (Active login session reference)
- `session_status` (`active`, `revoked`, `expired`)
- `online_status` (`online` if heartbeat fresh, else `offline`)
- `login_timestamp` (UTC)
- `last_heartbeat_timestamp` (UTC)
- `entitlement_tier` / `subscription_status`

### 3.3 Permitted Administrative Operations
- `REVOKE_SESSION`: Invalidate authentication session in database.
- `KICK_SESSION`: Force token expiration.
- `UPDATE_ENTITLEMENT`: Modify user subscription status.

---

## 4. Explicitly Prohibited Web & Backend Information

Under **NO circumstances** may any component transmit, collect, or store the following client data on Backend servers or display it in Admin Web interfaces:

### 4.1 Client Process & System Metadata
- โ Game Process ID (`pso2.exe` PID)
- โ Core Process ID (`NekoProxyCore` PID)
- โ Helper Process IDs (`v2ray-sn.exe` PID, `netfilter` handles)
- โ Local SOCKS5 listening port (e.g. `2801`)
- โ Running process list, process trees, or parent processes
- โ Process command lines or startup arguments
- โ Local operating system environment variables or file paths

### 4.2 Network Traffic & Destination History
- โ Destination IP address history
- โ Destination hostname / domain query history
- โ DNS query logs or DNS resolution requests
- โ Active TCP connection lists or historical socket tables
- โ Active UDP flow tables or packet counts
- โ Per-destination, per-flow, or per-process RX/TX breakdown
- โ Game server endpoint IP addresses contacted by the user

### 4.3 Packet Content & Diagnostics
- โ Raw packet payloads, decrypted streams, or frame dumps
- โ Packet capture files (PCAP) or packet header logs
- โ NetFilter raw event streams or hook callbacks
- โ Core internal debug logs, verbose trace files, or stack traces
- โ Memory dumps of Core, Launcher, or game client

### 4.4 Secrets & Authentication Material
- โ Shadowsocks passwords, encryption keys, or pre-shared keys
- โ JWT tokens, refresh tokens, Supabase service keys
- โ Launch permit cryptographic signatures or private verification keys
- โ Recovery codes, passwords, or hashes

---

## 5. Collection Boundary Rule (Data Minimization)

> [!CRITICAL]
> **Data must NOT be collected merely because it is hidden in the Web UI.**
>
> The privacy boundary is a **TRANSMISSION AND COLLECTION LIMIT**, not a presentation filter. Client software MUST NOT package detailed machine metrics into API requests with the expectation that the backend simply ignores or hides them.

```text
[ PROHIBITED PATTERN ]
Client Machine โ”€โ”€(Detailed Telemetry)โ”€โ”€> Backend API โ”€โ”€(UI Filters Out Metrics)โ”€โ”€> Admin Web
                                            โ”
                                    (Stored in DB / Logs) โ PRIVACY BREACH

[ MANDATORY PATTERN ]
Client Machine โ”€โ”€(Local IPC)โ”€โ”€> Launcher UI (Local Only)
Client Machine โ”€โ”€(Minimal Heartbeat: session_id, time)โ”€โ”€> Backend API
Japan VPS      โ”€โ”€(Aggregate Metrics)โ”€โ”€> Backend API โ”€โ”€> Admin Web โ… PRIVACY PRESERVED
```

---

## 6. Server Monitoring Origin Requirement

Server aggregate statistics MUST be collected **at the server**:

```text
Japan VPS
   โ”
   โ–ผ
Server-Side Monitoring Agent (System metrics exporter)
   โ”
   โ–ผ
Metrics / Aggregator API Endpoint
   โ”
   โ–ผ
Supabase / Backend Store
   โ”
   โ–ผ
Admin Web Dashboard
```

Aggregating client-side upload/download reports on the backend to construct server traffic charts is strictly prohibited, as it creates an incentive to track individual client throughput.

---

## 7. Client Heartbeat Data Minimization

The periodic heartbeat sent by Launcher to the backend must be minimal and strictly confined to maintaining session liveness:

```json
{
  "session_id": "019163ab-8f92-7411-b0e2-7634891b0123",
  "client_version": "1.0.0-debug",
  "heartbeat_utc": "2026-08-17T02:35:00.000Z"
}
```

The heartbeat payload MUST NOT contain network speed, bytes transferred, process status, destination IPs, or proxy statistics.

---

## 8. Session Management Boundary (No Remote Execution)

The Admin Web possesses **Session Authority**, not **Machine Control**.

| Allowed Admin Action | Mechanism | Prohibited Admin Action |
|---|---|---|
| Revoke User Session | Marks session invalid in auth DB; client terminates gracefully upon next token check. | Remote `KILL_GAME_PROCESS` command sent to client machine |
| Kick User Session | Deletes session token; triggers local logout on client. | Remote `KILL_CORE_PID` or `STOP_SERVICE` command |
| Disable Entitlement | Updates subscription status in database. | Remote file inspection, registry reading, or command execution |

---

## 9. Comprehensive Data Classification Matrix

| Data Item | Layer 1: Core | Layer 2: Launcher | Layer 3: Backend | Layer 3: Admin Web | Notes |
|---|:---:|:---:|:---:|:---:|---|
| **Core / Game / v2ray PID** | โ… Local | โ ๏ธ Local UI Only | โ FORBIDDEN | โ FORBIDDEN | Used strictly for local process monitoring |
| **Local SOCKS Port** | โ… Local | โ ๏ธ Local UI Only | โ FORBIDDEN | โ FORBIDDEN | `127.0.0.1:2801` internal to machine |
| **NetFilter Counters** | โ… Local | โ ๏ธ Local UI Only | โ FORBIDDEN | โ FORBIDDEN | Aggregated locally |
| **TCP / UDP / DNS Counters** | โ… Local | โ ๏ธ Local UI Only | โ FORBIDDEN | โ FORBIDDEN | Aggregated locally |
| **Local RX / TX Total** | โ… Local | โ… Local UI | โ FORBIDDEN | โ FORBIDDEN | Shown to user on their own screen |
| **Local Ping (to SS)** | โ… Local | โ… Local UI | โ FORBIDDEN | โ FORBIDDEN | Real-time user latency meter |
| **Core Debug Logs** | โ… Local File | โ ๏ธ Local View | โ FORBIDDEN | โ FORBIDDEN | Diagnostics stored on client disk only |
| **Server Aggregate RX/TX** | โ None | โ ๏ธ View Only | โ… Collected | โ… Displayed | Originates from VPS agent |
| **Server Ping / Loss** | โ None | โ ๏ธ View Only | โ… Collected | โ… Displayed | VPS aggregate health |
| **User ID / Account** | โ ๏ธ Auth Context | โ… User Profile | โ… Stored | โ… Managed | Identity authority |
| **Session State** | โ ๏ธ Auth State | โ… Auth State | โ… Stored | โ… Managed | Liveness authority |
| **Minimal Heartbeat** | โ None | โ… Emitted | โ… Received | โ… Displayed | Timestamp & session ID only |
| **Subscription Tier** | โ ๏ธ Permit Claim | โ… User Profile | โ… Stored | โ… Managed | Access entitlement |

---

## 10. Incident Diagnostics & Privacy Policy

If a user encounters a game connection issue and requires technical assistance:
1. **User-Initiated Diagnostic Export**: The Launcher may provide an explicit "Export Diagnostic Report" button.
2. **Local Artifact Generation**: Diagnostics are saved to a local zip/text file on the user's desktop.
3. **User Inspection & Redaction**: The user can review the diagnostic text before choosing to share it with support staff manually.
4. **No Automated Harvesting**: Core and Launcher MUST NEVER automatically upload diagnostic dumps or error reports in the background.

---

## 11. Privacy Regression Gate

For all future development phases (T1 through T6), every code review and pull request MUST pass the Privacy Gate:

```text
====================================================================
NEKO FAMILY PROXY โ€” PRIVACY REGRESSION CHECKLIST
====================================================================
[ ] 1. Does this change introduce any new fields to the Backend API?
       -> If YES, verify that no client PII, PIDs, flows, or logs are included.
[ ] 2. Does the Launcher heartbeat payload remain minimal?
[ ] 3. Are all new Core metrics confined to \\.\pipe\NekoProxyCoreTelemetry?
[ ] 4. Does the Admin Web query server statistics ONLY from the server monitoring agent?
[ ] 5. Are all credentials, tokens, and cryptographic keys completely redacted from logs?
====================================================================
```

If any item fails: **THE RELEASE IS BLOCKED IMMEDIATELY.**
