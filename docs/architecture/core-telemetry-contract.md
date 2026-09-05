# NEKO FAMILY PROXY โ€” ARCHITECTURE CONTRACT
# Core Telemetry Contract

```text
DOCUMENT:               docs/architecture/core-telemetry-contract.md
STATUS:                 FROZEN (T0 Contract Freeze)
CONTRACT VERSION:       1
SCHEMA VERSION:         1
OWNER:                  TEAM_COORDINATION
IMPLEMENTATION OWNER:   TEAM_CORE
LOCAL CONSUMER:         TEAM_LAUNCHER
REMOTE CONSUMER:        NONE (Strictly Local)
DATE:                   2026-08-17
```

---

## 1. Purpose

This document establishes the authoritative technical contract for the internal telemetry and observability subsystem of **NekoProxyCore**.

The primary purpose of this contract is to allow Core to publish real-time health snapshots, traffic counters, and lifecycle state events to the local **Launcher** (and local diagnostic tools) via an asynchronous, non-blocking IPC channel without introducing latency, stability risks, or failure coupling into the PSO2 proxy data-plane.

This is a **Local Observability Contract**. It is explicitly **NOT**:
- A remote Backend or Web API
- An authentication or authorization protocol
- A configuration mutation channel
- A remote command execution or management protocol

---

## 2. Contract Scope

### 2.1 In Scope (Local Observability)
- Core runtime lifecycle state (`Starting`, `Running`, `Stopping`, `Stopped`, `Failed`)
- Proxy data-plane state (`Disconnected`, `Connecting`, `Connected`, `Degraded`)
- Sub-process and component health (`v2ray-sn.exe`, local SOCKS5 listener, Shadowsocks upstream)
- NetFilter / Redirector aggregate counters (TCP, UDP, DNS, Redirects, Errors)
- Monotonic data transfer accounting (total `rx_bytes`, total `tx_bytes`)
- Structured error events (redacted, categorized, severity-tagged)
- Periodic health snapshots (`core.health.snapshot`)
- IPC buffer and drop accounting (`dropped_telemetry_events`)
- Local diagnostic process metadata (`core_pid`, `v2ray_pid`, `game_pid` โ€” local only)

### 2.2 Out of Scope (Explicitly Prohibited)
- Raw packet payloads, frame captures, or PCAP streams
- Network packet inspection or destination tracking
- Authentication tokens, JWTs, Supabase keys, Launch Permits
- Shadowsocks passwords or preshared keys
- Remote control, process termination, or configuration injection
- Forwarding of internal telemetry to remote web servers or external endpoints

---

## 3. Architecture & Data Flow

```text
+---------------------------------------------------------------------------------+
|                                 NekoProxyCore                                   |
|                                                                                 |
|  [ Network Hot Path ]                                                          |
|  pso2.exe -> NetFilter -> Redirector -> Local SOCKS5 -> v2ray-sn -> SS Upstream|
|        โ”            โ”             โ”            โ”                                |
|   (Atomic Inc)  (Atomic Inc)  (Atomic Inc) (Status Poll)                        |
|        โ”            โ”             โ”            โ”                                |
|        โ–ผ            โ–ผ             โ–ผ            โ–ผ                                |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|  |             Telemetry Event Producers             |                          |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|                           โ”                                                     |
|                           โ–ผ (Decoupled Queue / Atomics)                         |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|  |           Telemetry Stats Aggregator              |                          |
|  |     - Accumulates counters                        |                          |
|  |     - Generates periodic snapshots (e.g. 1000ms)  |                          |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|                           โ”                                                     |
|                           โ–ผ (Bounded Enqueue)                                   |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|  | Bounded Telemetry Buffer (Ring Buffer / FIFO)     |                          |
|  | (Policy: Drop oldest/newest on full + count drop) |                          |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|                           โ”                                                     |
|                           โ–ผ (Async Publisher)                                   |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|  | Local Named Pipe Server                           |                          |
|  | Endpoint: \\.\pipe\NekoProxyCoreTelemetry         |                          |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
+---------------------------โ”-----------------------------------------------------+
                            โ” (Local Windows Named Pipe IPC - Read Only)
                            โ–ผ
+---------------------------------------------------------------------------------+
|                                 NekoLauncher                                    |
|                                                                                 |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|  | Local Telemetry Consumer Client                   |                          |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|                           โ”                                                     |
|                           โ–ผ                                                     |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
|  | Launcher Local UI Dashboard & Status Meters       |                          |
|  | (Ping, Transfer, Speed, Core Health, Uptime)     |                          |
|  +โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€+                          |
+---------------------------------------------------------------------------------+
```

---

## 4. Control vs Telemetry Pipe Separation

To enforce strict privilege separation and failure isolation, Core provides two independent Named Pipe endpoints:

| Property | Control Pipe | Telemetry Pipe |
|---|---|---|
| **Pipe Name** | `\\.\pipe\NekoProxyCoreControl` | `\\.\pipe\NekoProxyCoreTelemetry` |
| **Directionality** | Bidirectional (Duplex) | Unidirectional Server-to-Client (Publisher) |
| **Primary Purpose** | Core lifecycle, authorization, permit delivery | Observability, health snapshots, metrics, error events |
| **Allowed Commands** | `Start`, `Stop`, `VerifyPermit`, `QueryStatus` | **NONE** (Read-Only Stream) |
| **Secret Transport** | Encrypted/Protected Authorization Permits | **STRICTLY FORBIDDEN** (No secrets, no tokens) |
| **Failure Consequence** | Core cannot be controlled / starts failed | **ZERO IMPACT** on Proxy; proxy continues running |
| **Consumer Access** | Privileged Local Launcher Process | Local Launcher UI & Local Diagnostic Utilities |

### Invariant Rules
1. Telemetry Pipe MUST NEVER accept incoming state mutations or administrative commands.
2. Control Pipe MUST NOT be saturated with high-frequency telemetry data.
3. The existence, crash, or disconnection of a telemetry listener MUST NOT alter Control Pipe state or proxy functionality.

---

## 5. Failure Isolation Invariant

The fundamental resilience invariant of the telemetry subsystem is:

```text
TELEMETRY_FAILURE MUST NOT CAUSE PROXY_FAILURE
```

### Mandatory Failure Handling Policies:
1. **Telemetry Consumer Disconnection**: If Launcher closes or crashes, Core's publisher simply detects broken pipe, cleans up the client handle, logs a debug trace, and continues proxying.
2. **Slow Consumer (Backpressure)**: If the client does not read data fast enough, Core's bounded buffer will overflow. Core MUST drop telemetry packets, increment `dropped_telemetry_events`, and NEVER block the sender thread.
3. **Pipe Initialization Failure**: If OS limits prevent creating `\\.\pipe\NekoProxyCoreTelemetry`, Core MUST log a warning and proceed with network routing.
4. **Serialization Error**: If an event fails to serialize to JSON, the error is recorded locally, the event is discarded, and network forwarding continues unimpeded.

---

## 6. Hot-Path Performance Rules

The network proxy hot path (handling thousands of packets per second) must remain untouched by heavy observability overhead.

### Prohibited on Packet Processing Threads:
- โ Synchronous Named Pipe writes (`FileStream.Write`, `NamedPipeServerStream.Write`)
- โ Synchronous disk file I/O or database operations
- โ JSON object allocation, reflection, or text serialization
- โ Acquisition of blocking locks (`Monitor.Enter`, `lock`, mutexes with indefinite wait)
- โ Allocating objects per packet that trigger Garbage Collection pressure

### Permitted on Packet Processing Threads:
- โ… Lock-free atomic counter increments (`Interlocked.Increment`, `Interlocked.Add`)
- โ… Non-blocking timestamp capture (`Stopwatch.GetTimestamp`)
- โ… Writing to lock-free or bounded thread-safe structures if required

### Decoupled Aggregator Flow:
1. Fast atomic counters increment on the packet thread.
2. An independent background worker (polling at a fixed interval, e.g. 500ms to 1000ms) takes a consistent read of atomic counters.
3. The background worker constructs the `core.health.snapshot` payload, serializes it, and enqueues it to the publisher buffer.

---

## 7. Message Envelope Schema

All messages published over `\\.\pipe\NekoProxyCoreTelemetry` MUST adhere to the standardized JSON envelope.

### Schema:
```json
{
  "schema_version": 1,
  "sequence": 1042,
  "timestamp_utc": "2026-08-17T02:30:15.123Z",
  "message_type": "core.health.snapshot",
  "component": "core",
  "payload": {}
}
```

### Common Fields:
| Field | Type | Description | Mandatory |
|---|---|---|---|
| `schema_version` | integer | Major schema version number (currently `1`). | Yes |
| `sequence` | integer (uint64) | Strictly increasing monotonic sequence number per publisher session. | Yes |
| `timestamp_utc` | string (ISO-8601) | UTC timestamp with millisecond precision when event was published. | Yes |
| `message_type` | string | Dot-separated identifier defining payload semantics. | Yes |
| `component` | string | Originating subsystem (`core`, `netfilter`, `redirector`, `v2ray`, `dns`). | Yes |
| `payload` | object | Message-specific structured JSON object. | Yes |

---

## 8. Schema Versioning & Evolution Rules

- Initial Schema Version: `schema_version = 1`.
- **Additive Changes (Compatible)**: Adding new fields to `payload` is considered backwards-compatible. Consumers MUST ignore unknown properties (`JsonIgnoreCondition` / lenient parsing).
- **Breaking Changes**: Renaming fields, altering field types, or removing fields requires incrementing `schema_version` (e.g. to `2`), joint review across `TEAM_CORE` and `TEAM_LAUNCHER`, and formal approval from `TEAM_COORDINATION`.

---

## 9. Core Health Snapshot Specification

- **Message Type**: `core.health.snapshot`
- **Cadence**: Periodic (default: every `1000ms`, configurable down to `500ms`).
- **Component**: `core`

### Payload Schema:
```json
{
  "schema_version": 1,
  "sequence": 45,
  "timestamp_utc": "2026-08-17T02:30:01.000Z",
  "message_type": "core.health.snapshot",
  "component": "core",
  "payload": {
    "core_state": "running",
    "proxy_state": "connected",
    "uptime_ms": 125000,

    "tcp_connect_total": 412,
    "tcp_active": 8,
    "tcp_closed_total": 404,

    "udp_event_total": 95,

    "dns_query_total": 64,
    "dns_failure_total": 0,

    "redirect_success_total": 412,
    "redirect_failure_total": 0,

    "rx_bytes": 154820912,
    "tx_bytes": 12490184,

    "network_error_total": 0,

    "v2ray_running": true,
    "local_socks_running": true,
    "shadowsocks_connected": true,

    "dropped_telemetry_events": 0
  }
}
```

### Health Snapshot Payload Field Definitions:
| Field Name | Type | Description |
|---|---|---|
| `core_state` | string | `starting`, `running`, `stopping`, `stopped`, `failed` |
| `proxy_state` | string | `disconnected`, `connecting`, `connected`, `degraded`, `stopped` |
| `uptime_ms` | integer (uint64) | Milliseconds elapsed since Core process start |
| `tcp_connect_total` | integer (uint64) | Total TCP connection attempts intercepted |
| `tcp_active` | integer (uint32) | Current open/active TCP connections |
| `tcp_closed_total` | integer (uint64) | Total TCP connections closed/terminated |
| `udp_event_total` | integer (uint64) | Total UDP packets/flows intercepted |
| `dns_query_total` | integer (uint64) | Total DNS lookups handled |
| `dns_failure_total` | integer (uint64) | Total failed DNS resolutions |
| `redirect_success_total` | integer (uint64) | Successful redirection events to local SOCKS5 |
| `redirect_failure_total` | integer (uint64) | Failed redirection events |
| `rx_bytes` | integer (uint64) | Total inbound bytes received across proxy |
| `tx_bytes` | integer (uint64) | Total outbound bytes transmitted across proxy |
| `network_error_total` | integer (uint64) | Total low-level network errors |
| `v2ray_running` | boolean | True if `v2ray-sn.exe` process is active |
| `local_socks_running` | boolean | True if `127.0.0.1:2801` is accepting connections |
| `shadowsocks_connected` | boolean | True if upstream Shadowsocks handshake is healthy |
| `dropped_telemetry_events`| integer (uint64) | Cumulative telemetry events dropped due to buffer overflow |

---

## 10. Lifecycle State & Diagnostic Events

Lifecycle events are published on distinct state transitions (not periodically).

### 10.1 Recognized Event Message Types:
- `core.started` / `core.stopping` / `core.stopped` / `core.failed`
- `proxy.starting` / `proxy.running` / `proxy.degraded` / `proxy.stopped`
- `v2ray.starting` / `v2ray.started` / `v2ray.failed` / `v2ray.exited`
- `socks.listening` / `socks.failed`
- `shadowsocks.connected` / `shadowsocks.disconnected`
- `netfilter.started` / `netfilter.stopped` / `netfilter.error`

### 10.2 Structured Error Event Example (`component.error`):
```json
{
  "schema_version": 1,
  "sequence": 112,
  "timestamp_utc": "2026-08-17T02:31:40.500Z",
  "message_type": "component.error",
  "component": "redirector",
  "payload": {
    "error_code": "REDIRECT_TARGET_UNREACHABLE",
    "severity": "warning",
    "recoverable": true,
    "message": "Local SOCKS5 listener did not accept connection within timeout",
    "context": {
      "target_port": 2801,
      "retry_count": 1
    }
  }
}
```

> [!CAUTION]
> Error events MUST NEVER include raw packet bytes, IP destination history, user tokens, or credentials in their `context` or `message` fields.

---

## 11. Bounded Buffer & Drop Policy

The telemetry producer MUST employ a bounded buffer:
- **Maximum Buffer Capacity**: Configurable, default `256` messages.
- **Drop Policy**: `DROP_OLDEST` (Default). When the buffer is full and a new snapshot arrives, the oldest pending snapshot in the queue is discarded.
- **Accounting**: Every dropped event atomically increments `dropped_telemetry_events`.
- **Producer Non-Blocking Guarantee**: Calling `Enqueue()` on the buffer MUST return immediately (zero wait time).

---

## 12. Windows Named Pipe Security

The Named Pipe `\\.\pipe\NekoProxyCoreTelemetry` MUST be configured with explicit Windows Security Descriptors:
1. **Local Access Only**: Named pipe creation flags MUST NOT enable network sharing or remote RPC.
2. **Access Control List (ACL)**: Grant Read permissions to the Current Interactive User and Local System / Administrators.
3. **Pipe Mode**: `PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_NOWAIT` or managed equivalent (`NamedPipeServerStream` with asynchronous completion).

---

## 13. Local Consumer (Launcher) Requirements

`TEAM_LAUNCHER` must implement the consumer according to the following invariants:
1. **Resilience to Core Absence**: Launcher must start and function normally even if Core is not running or telemetry pipe is missing.
2. **Auto-Reconnection**: If the telemetry pipe closes, Launcher enters a retry loop with exponential backoff (e.g. 1s, 2s, 5s) without crashing.
3. **Lenient Deserialization**: Launcher must ignore unknown JSON fields and unknown `message_type` values gracefully.
4. **Local Data Confinement**: Launcher MUST NOT upload telemetry snapshots to Backend APIs, Supabase tables, or external analytics services.

---

## 14. Acceptance Criteria & Test Matrix

Phase contracts touching this specification may close only when the following test suite passes:

```text
[PASS] Telemetry pipe initializes successfully on Core startup
[PASS] Launcher connects and receives valid core.health.snapshot within 2 seconds
[PASS] Killing Launcher does NOT crash or degrade Core proxy performance
[PASS] Simulating slow consumer (pausing reader) triggers event drop without blocking proxy
[PASS] Atomic counter increments under simulated 5000 pkt/sec load add < 0.05ms latency
[PASS] Core shutdown cleanly terminates publisher and closes pipe handle
[PASS] No prohibited secrets or PII found in serialized telemetry output
[PASS] Real PSO2 game session verified with telemetry pipe active
```
