# NEKO FAMILY PROXY — ARCHITECTURE DESIGN
# NetFilter & Redirector Statistics Instrumentation Design (Phase T2A)

```text
DOCUMENT:               docs/architecture/netfilter-statistics-instrumentation.md
STATUS:                 FROZEN (T2A Design Approved)
DESIGN PHASE:           T2A — NATIVE STATISTICS SOURCE MAPPING & INSTRUMENTATION DESIGN
OWNER:                  TEAM_CORE
SUPPORTING:             TEAM_COORDINATION
TARGET CONSUMERS:       TEAM_CORE (T2B Implementation), TEAM_LAUNCHER (T3 Consumer)
DATE:                   2026-08-17
```

---

## 1. Executive Summary & Objectives

This document specifies the authoritative source mapping, concurrency model, hot-path safety invariants, and native-to-managed bridge architecture for **Phase T2 NetFilter Statistics** within NekoProxyCore.

### Core Objectives
1. **Authoritative Origin Mapping**: Pinpoint the exact C++ source functions and managed entry points where each metric originates.
2. **Hot-Path Zero Overhead**: Enforce strict non-blocking, lock-free, zero-allocation accounting on native packet and connection threads.
3. **Accurate RX/TX Layer Definition**: Eliminate double-counting and ambiguity by strictly defining the accounting layer at the Application TCP/UDP Payload boundary.
4. **Clean Native Bridge**: Define a fixed-layout, zero-allocation C-struct snapshot queried periodically by the managed `CoreTelemetryAggregator`.
5. **Inviolate Privacy Boundary**: Guarantee that all native telemetry metrics are aggregate counters that never inspect, store, or transmit domain names, IPs, PIDs, or packet payloads.

---

## 2. Architecture & Data Flow Overview

```text
+--------------------------------------------------------------------------------------------------+
| NATIVE DATA PLANE (Redirector.bin / netfilter2)                                                  |
|                                                                                                  |
|  [TCP Hot Path]                                                                                  |
|  pso2.exe -> tcpConnectRequest() -> TCPHandler::Accept() -> TCPHandler::Handle()                 |
|                   │                                               │                              |
|             (Atomic Inc)                                    (Atomic Inc)                         |
|                   │                                               │                              |
|                   ▼                                               ▼                              |
|           tcp_connect_total                             redirect_success_total                   |
|                                                         tcp_active (Inc/Dec)                     |
|                                                         rx_bytes / tx_bytes                      |
|                                                         tcp_closed_total                         |
|                                                                                                  |
|  [UDP & DNS Hot Path]                                                                            |
|  pso2.exe -> udpSend() -> DNSHandler::CreateHandler() -> dns_query_total / dns_failure_total     |
|                       └──> SocksHelper::UDP::Send()    -> udp_event_total / rx_bytes / tx_bytes    |
|                                                                                                  |
|  All Counters: std::atomic<uint64_t> in Native Memory Space                                      |
+--------------------------------------------------------------------------------------------------+
                                                 │
                                                 │  aio_getStats(&stats)  [P/Invoke struct copy]
                                                 │  (Polled on 1000ms background tick - NO Hot Path)
                                                 ▼
+--------------------------------------------------------------------------------------------------+
| MANAGED AGGREGATION & TELEMETRY (NekoProxyCore.Core / Host)                                      |
|                                                                                                  |
|  INetFilterStatisticsProvider -> CoreTelemetryAggregator (1000ms PeriodicTimer)                  |
|                                           │                                                      |
|                                           ▼                                                      |
|                              CoreHealthSnapshotPayload                                           |
|                                           │                                                      |
|                                           ▼                                                      |
|                             BoundedTelemetryBuffer (Ring FIFO)                                   |
|                                           │                                                      |
|                                           ▼                                                      |
|                         Named Pipe: \\.\pipe\NekoProxyCoreTelemetry                              |
+--------------------------------------------------------------------------------------------------+
```

---

## 3. Authoritative Metric Definitions & Source Mapping

### 3.1 TCP Metrics

#### `tcp_connect_total` (Intercepted TCP Connection Attempts)
- **Semantic Meaning**: Total number of outbound TCP connection attempts intercepted for proxying from the targeted game process (`pso2.exe`).
- **Authoritative File**: `Redirector/EventHandler.cpp`
- **Authoritative Function**: `void tcpConnectRequest(ENDPOINT_ID id, PNF_TCP_CONN_INFO info)`
- **Thread Context**: NetFilter SDK worker thread pool (`NF_EventHandler`).
- **Increment Point**: Immediately after process filtering validation (`checkHandleName(info->processId)` passes) and destination address redirection rewrite.
- **Can Double-Count**: `NO` (Each OS connection triggers exactly one `tcpConnectRequest`).
- **Can Miss Events**: `NO` (Kernel driver indicates every connection matching rule).
- **Hot-Path**: `YES` (Increment must be `std::atomic<uint64_t>::fetch_add(1)`).
- **Expected Cost**: `LOW` (< 5 ns CPU cycle).

#### `tcp_active` (Current Open Redirected TCP Connections)
- **Semantic Meaning**: Number of currently open, active TCP proxy tunnels relaying traffic between the game and the local SOCKS5 proxy.
- **Authoritative File**: `Redirector/TCPHandler.cpp`
- **Authoritative Function**: `void TCPHandler::Handle(SOCKET client)`
- **Thread Context**: Dedicated per-connection detached worker thread (`TCPHandler::Handle`).
- **Increment Point**: When `remote->Connect(&target)` succeeds (SOCKS5 handshake established).
- **Decrement Point**: When `TCPHandler::Handle` finishes and closes `client` and `remote` sockets.
- **Can Double-Count**: `NO` (Paired increment/decrement in RAII/structured lifecycle).
- **Can Miss Events**: `NO`.
- **Hot-Path**: `YES` (Increment/Decrement via atomic primitive).
- **Expected Cost**: `LOW`.

#### `tcp_closed_total` (Total Closed Redirected TCP Connections)
- **Semantic Meaning**: Cumulative count of redirected TCP connections that have closed or terminated.
- **Authoritative File**: `Redirector/TCPHandler.cpp`
- **Authoritative Function**: `void TCPHandler::Handle(SOCKET client)`
- **Thread Context**: Connection worker thread.
- **Increment Point**: At the exit of `TCPHandler::Handle` after `closesocket(client)` and `delete remote`.
- **Invariant**: Over time, `tcp_connect_total == tcp_active + tcp_closed_total + redirect_failure_total`.
- **Hot-Path**: `YES` (Atomic increment).
- **Expected Cost**: `LOW`.

---

### 3.2 UDP Metrics

#### `udp_event_total` (Intercepted UDP Datagrams)
- **Semantic Meaning**: Total outbound UDP datagrams intercepted from the targeted game process for redirection to local SOCKS5 UDP relay.
- **Authoritative File**: `Redirector/EventHandler.cpp`
- **Authoritative Function**: `void udpSend(ENDPOINT_ID id, const unsigned char* target, const char* buffer, int length, PNF_UDP_OPTIONS options)`
- **Thread Context**: NetFilter SDK callback thread.
- **Increment Point**: On entry of non-DNS UDP routing path before `remote->Send()`.
- **Can Double-Count**: `NO`.
- **Can Miss Events**: `NO`.
- **Hot-Path**: `YES` (Atomic increment).
- **Expected Cost**: `LOW`.

---

### 3.3 DNS Metrics

#### `dns_query_total` (Intercepted DNS Queries)
- **Semantic Meaning**: Total DNS queries intercepted and forwarded to the configured DNS resolver (direct or proxied).
- **Authoritative File**: `Redirector/DNSHandler.cpp`
- **Authoritative Function**: `void DNSHandler::CreateHandler(ENDPOINT_ID id, PSOCKADDR_IN6 target, const char* packet, int length, PNF_UDP_OPTIONS options)`
- **Thread Context**: NetFilter callback thread.
- **Increment Point**: At entry of `DNSHandler::CreateHandler` before dispatching query thread.
- **Can Double-Count**: `NO`.
- **Can Miss Events**: `NO`.
- **Hot-Path**: `YES` (Atomic increment).
- **Expected Cost**: `LOW`.

#### `dns_failure_total` (Failed DNS Queries)
- **Semantic Meaning**: Total DNS resolutions that failed due to socket errors, send failure, or timeout (4-second `select` expiry) without returning a response to NetFilter.
- **Authoritative File**: `Redirector/DNSHandler.cpp`
- **Authoritative Function**: `HandleClientDNS` and `HandleRemoteDNS`
- **Thread Context**: Detached DNS query worker thread.
- **Increment Point**: In error handling blocks when `select` returns 0 (timeout), `SOCKET_ERROR`, or `recvfrom` fails.
- **Can Double-Count**: `NO`.
- **Can Miss Events**: `NO`.
- **Hot-Path**: `NO` (Failure path only).
- **Expected Cost**: `LOW`.

---

### 3.4 Redirection Success & Failure Metrics

#### `redirect_success_total`
- **Semantic Meaning**: Aggregate count of successful local SOCKS handoff operations (TCP SOCKS5 handshake established + UDP datagrams successfully submitted to local SOCKS UDP relay).
- **Exact Scope**: Local SOCKS handoff operations (not game sessions, not remote Shadowsocks sessions).
- **Authoritative File**: `Redirector/TCPHandler.cpp` & `Redirector/EventHandler.cpp`
- **Authoritative Function**: `TCPHandler::Handle` (`remote->Connect` == true) and `EventHandler.cpp` (`remote->Send` == length).
- **Thread Context**: Connection worker / callback thread.
- **Increment Point**: Immediately upon confirmed handshake/send completion.
- **Hot-Path**: `YES` (Atomic increment).
- **Expected Cost**: `LOW`.

#### `redirect_failure_total`
- **Semantic Meaning**: Aggregate count of failed local SOCKS handoff operations (destination lookup failure, SOCKS connect failure, authentication failure, or UDP associate failure).
- **Exact Scope**: Failed local SOCKS handoff operations.
- **Authoritative File**: `Redirector/TCPHandler.cpp`, `Redirector/EventHandler.cpp`, `Redirector/SocksHelper.cpp`
- **Authoritative Function**: `TCPHandler::Handle` (target not found in `tcpContext` or `!remote->Connect(&target)`), `udpSend` (`!remote->Associate()`).
- **Thread Context**: Connection worker / callback thread.
- **Increment Point**: On any failure branch resulting in dropped/aborted redirection. Counted once at the owning failure boundary.
- **Hot-Path**: `NO` (Failure path only).
- **Expected Cost**: `LOW`.

---

### 3.5 RX / TX Byte Accounting (Application Payload Layer)

```text
+-------------------------------------------------------------------------+
| OSI Layer Model & Byte Accounting Boundary                              |
+-------------------------------------------------------------------------+
| [Layer 7: Game Application Payload] <--- AUTHORITATIVE COUNTING LAYER   |
|   - TCPHandler::Send / TCPHandler::Read payload buffer bytes            |
|   - SOCKS UDP payload buffer bytes (excluding 10/22 byte SOCKS header)  |
|   - DNS query/response payload bytes                                    |
+-------------------------------------------------------------------------+
| [Layer 5: SOCKS5 Protocol Framing]  (Excluded from RX/TX)               |
+-------------------------------------------------------------------------+
| [Layer 4: Shadowsocks AEAD Crypto]  (Excluded from RX/TX)               |
+-------------------------------------------------------------------------+
| [Layer 3: IP/Network Interface]     (Excluded from RX/TX)               |
+-------------------------------------------------------------------------+
```

#### Byte Accounting Specification
- **Counting Layer**: `APPLICATION_PAYLOAD` (L7 Application Data).
- **Includes Protocol Overhead**: `NO` (No IP/TCP headers, no SOCKS framing bytes, no Shadowsocks encryption headers).
- **Direction**:
  - `tx_bytes`: Outbound bytes sent from `pso2.exe` to proxy/upstream.
  - `rx_bytes`: Inbound bytes received from proxy and delivered to `pso2.exe`.
- **Authoritative Hook Points**:
  1. **TCP TX**: In `TCPHandler::Send` after `recv(client, buffer, sizeof(buffer), 0)` returns `length > 0`:
     `tx_bytes.fetch_add(length, std::memory_order_relaxed);`
  2. **TCP RX**: In `TCPHandler::Read` after `remote->Read(buffer, sizeof(buffer))` returns `length > 0`:
     `rx_bytes.fetch_add(length, std::memory_order_relaxed);`
  3. **UDP TX**: In `EventHandler.cpp:udpSend` after `remote->Send(...)` returns `length > 0`:
     `tx_bytes.fetch_add(length, std::memory_order_relaxed);`
  4. **UDP RX**: In `EventHandler.cpp:udpReceiveHandler` after `remote->Read(...)` returns payload `length > 0`:
     `rx_bytes.fetch_add(length, std::memory_order_relaxed);`
  5. **DNS TX/RX**: In `DNSHandler.cpp` for query `length` (TX) and response `size` (RX).
- **Double-Count Risk**: `LOW_BUT_REQUIRES_VALIDATION` (Each byte must have exactly one owning accounting boundary; tested and proven by implementation).
- **Overflow Safety**: 64-bit unsigned integers (`uint64_t`). Will not overflow under continuous 10 Gbps throughput for > 400 years.
- **Performance Cost**: Sub-nanosecond atomic addition per read/write buffer block (~1446 bytes). Negligible CPU impact (< 0.001% CPU).

---

### 3.6 Network Error Metrics

#### `network_error_total`
- **Semantic Meaning**: Cumulative count of underlying socket or operating system network errors encountered during redirection, socket creation, binding, or data transfer.
- **Counting Policy**: `NETWORK_ERROR_COUNTING_POLICY = COUNT_AT_FAILURE_OWNING_BOUNDARY_ONLY`. A propagated error must not be counted at multiple stack layers (e.g. SocksHelper -> TCPHandler -> EventHandler).
- **Exclusions**: Normal EOF / connection shutdown (recv = 0), expected cancellation, WSAEINTR during controlled shutdown.
- **Authoritative Files**: `Redirector/TCPHandler.cpp`, `Redirector/SocksHelper.cpp`, `Redirector/DNSHandler.cpp`
- **Increment Point**: When WinSock API calls return `SOCKET_ERROR` or `INVALID_SOCKET` (excluding clean shutdown / expected EOF / 10004 `WSAEINTR`).
- **Hot-Path**: `NO` (Failure path only).
- **Expected Cost**: `LOW`.

---

## 4. Native-to-Managed Statistics Bridge Contract

### 4.1 C++ Native Structure (`Redirector/Based.h`)

```cpp
#pragma pack(push, 8)
typedef struct _NF_STATS {
    uint64_t tcp_connect_total;
    uint32_t tcp_active;
    uint32_t _reserved;          // Explicit 8-byte alignment padding
    uint64_t tcp_closed_total;
    uint64_t udp_event_total;
    uint64_t dns_query_total;
    uint64_t dns_failure_total;
    uint64_t redirect_success_total;
    uint64_t redirect_failure_total;
    uint64_t rx_bytes;
    uint64_t tx_bytes;
    uint64_t network_error_total;
} NF_STATS, *PNF_STATS;
#pragma pack(pop)
```

### 4.2 Native Export API (`Redirector/Redirector.cpp`)

```cpp
extern "C" {
    __declspec(dllexport) void __cdecl aio_getStats(NF_STATS* stats);
    __declspec(dllexport) void __cdecl aio_resetStats();
}
```

Implementation:
```cpp
void aio_getStats(NF_STATS* stats)
{
    if (!stats) return;
    stats->tcp_connect_total      = g_tcp_connect_total.load(std::memory_order_relaxed);
    stats->tcp_active             = g_tcp_active.load(std::memory_order_relaxed);
    stats->_reserved              = 0;
    stats->tcp_closed_total       = g_tcp_closed_total.load(std::memory_order_relaxed);
    stats->udp_event_total        = g_udp_event_total.load(std::memory_order_relaxed);
    stats->dns_query_total        = g_dns_query_total.load(std::memory_order_relaxed);
    stats->dns_failure_total      = g_dns_failure_total.load(std::memory_order_relaxed);
    stats->redirect_success_total = g_redirect_success_total.load(std::memory_order_relaxed);
    stats->redirect_failure_total = g_redirect_failure_total.load(std::memory_order_relaxed);
    stats->rx_bytes               = g_rx_bytes.load(std::memory_order_relaxed);
    stats->tx_bytes               = g_tx_bytes.load(std::memory_order_relaxed);
    stats->network_error_total    = g_network_error_total.load(std::memory_order_relaxed);
}

void aio_resetStats()
{
    g_tcp_connect_total.store(0, std::memory_order_relaxed);
    g_tcp_active.store(0, std::memory_order_relaxed);
    g_tcp_closed_total.store(0, std::memory_order_relaxed);
    g_udp_event_total.store(0, std::memory_order_relaxed);
    g_dns_query_total.store(0, std::memory_order_relaxed);
    g_dns_failure_total.store(0, std::memory_order_relaxed);
    g_redirect_success_total.store(0, std::memory_order_relaxed);
    g_redirect_failure_total.store(0, std::memory_order_relaxed);
    g_rx_bytes.store(0, std::memory_order_relaxed);
    g_tx_bytes.store(0, std::memory_order_relaxed);
    g_network_error_total.store(0, std::memory_order_relaxed);
}
```

### 4.3 Managed Interop Definition (`Netch/Interops/Redirector.cs`)

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct RedirectorStatistics
{
    public ulong TcpConnectTotal;
    public uint TcpActive;
    private uint _reserved;
    public ulong TcpClosedTotal;
    public ulong UdpEventTotal;
    public ulong DnsQueryTotal;
    public ulong DnsFailureTotal;
    public ulong RedirectSuccessTotal;
    public ulong RedirectFailureTotal;
    public ulong RxBytes;
    public ulong TxBytes;
    public ulong NetworkErrorTotal;
}

[DllImport(Redirector_bin, CallingConvention = CallingConvention.Cdecl)]
public static extern void aio_getStats(out RedirectorStatistics stats);

[DllImport(Redirector_bin, CallingConvention = CallingConvention.Cdecl)]
public static extern void aio_resetStats();
```

---

## 5. Managed Architecture & Provider Decoupling

To preserve testability across platforms (including Linux unit tests in CI), managed code introduces an abstract provider interface:

```csharp
namespace NekoProxyCore.Core;

public interface INetFilterStatisticsProvider
{
    NetFilterStatisticsSnapshot GetCurrentStatistics();
}

public readonly record struct NetFilterStatisticsSnapshot(
    ulong TcpConnectTotal,
    uint TcpActive,
    ulong TcpClosedTotal,
    ulong UdpEventTotal,
    ulong DnsQueryTotal,
    ulong DnsFailureTotal,
    ulong RedirectSuccessTotal,
    ulong RedirectFailureTotal,
    ulong RxBytes,
    ulong TxBytes,
    ulong NetworkErrorTotal);
```

### Managed Health Snapshot Payload Update (`CoreHealthSnapshotPayload.cs`)

The record will be extended to include all T0-frozen counter properties:

```csharp
namespace NekoProxyCore.Core;

public sealed record CoreHealthSnapshotPayload(
    [property: JsonPropertyName("core_state")] string CoreState,
    [property: JsonPropertyName("proxy_state")] string ProxyState,
    [property: JsonPropertyName("uptime_ms")] ulong UptimeMs,
    [property: JsonPropertyName("tcp_connect_total")] ulong TcpConnectTotal,
    [property: JsonPropertyName("tcp_active")] uint TcpActive,
    [property: JsonPropertyName("tcp_closed_total")] ulong TcpClosedTotal,
    [property: JsonPropertyName("udp_event_total")] ulong UdpEventTotal,
    [property: JsonPropertyName("dns_query_total")] ulong DnsQueryTotal,
    [property: JsonPropertyName("dns_failure_total")] ulong DnsFailureTotal,
    [property: JsonPropertyName("redirect_success_total")] ulong RedirectSuccessTotal,
    [property: JsonPropertyName("redirect_failure_total")] ulong RedirectFailureTotal,
    [property: JsonPropertyName("rx_bytes")] ulong RxBytes,
    [property: JsonPropertyName("tx_bytes")] ulong TxBytes,
    [property: JsonPropertyName("network_error_total")] ulong NetworkErrorTotal,
    [property: JsonPropertyName("v2ray_running")] bool V2RayRunning,
    [property: JsonPropertyName("local_socks_running")] bool LocalSocksRunning,
    [property: JsonPropertyName("shadowsocks_connected")] bool ShadowsocksConnected,
    [property: JsonPropertyName("dropped_telemetry_events")] ulong DroppedTelemetryEvents);
```

---

## 6. Counter Lifetime & Reset Policy

| Property | Rule / Behavior |
|---|---|
| **Counter Scope** | Proxy Session Lifetime |
| **Reset Trigger** | Native `aio_init()` (proxy session startup) and explicit `aio_resetStats()` call. Counters are preserved across `aio_free()` teardown until the next `aio_init()` reinitializes them to avoid worker callback shutdown races. |
| **Sequence Continuity** | Telemetry envelope `sequence` continues monotonically per Core process lifetime; payload counters reflect active proxy session totals. |
| **Zero State** | When proxy is stopped (`proxy_state = stopped`), counters report `0` (or are omitted / unavailable). |

---

## 7. Hot-Path & Resilience Guarantees

```text
================================================================================
HOT-PATH SAFETY AUDIT MATRIX
================================================================================
[PASS] Synchronous Named Pipe writes on packet processing thread?     NO (Prohibited)
[PASS] Synchronous file / disk I/O on packet processing thread?       NO (Prohibited)
[PASS] JSON serialization on packet processing thread?                 NO (Prohibited)
[PASS] Heap allocation per packet / connection event?                 NO (Zero alloc)
[PASS] Mutex / CriticalSection acquisition for counter increment?     NO (Atomic only)
[PASS] Packet payload inspection / deep packet logging?               NO (Prohibited)
================================================================================
```

---

## 8. Phase T2 Minimum Viable Set Decision

All 11 metrics specified in the T0 contract schema are supported by authoritative, low-overhead native hooks:

```text
================================================================================
T2 METRICS CLASSIFICATION
================================================================================
1.  tcp_connect_total          = IMPLEMENT_T2_NOW
2.  tcp_active                 = IMPLEMENT_T2_NOW
3.  tcp_closed_total           = IMPLEMENT_T2_NOW
4.  udp_event_total            = IMPLEMENT_T2_NOW
5.  dns_query_total            = IMPLEMENT_T2_NOW
6.  dns_failure_total          = IMPLEMENT_T2_NOW
7.  redirect_success_total     = IMPLEMENT_T2_NOW
8.  redirect_failure_total     = IMPLEMENT_T2_NOW
9.  rx_bytes                   = IMPLEMENT_T2_NOW
10. tx_bytes                   = IMPLEMENT_T2_NOW
11. network_error_total        = IMPLEMENT_T2_NOW
================================================================================
T2_DEFERRED_METRICS            = NONE
================================================================================
```

---

## 9. Implementation File Matrix for Phase T2B

### Native Files to Modify (`Redirector/`)
- `Redirector/Based.h`: Define `NF_STATS` struct with 64-bit alignment.
- `Redirector/Redirector.cpp`: Export `aio_getStats` and `aio_resetStats`; initialize/reset atomic counters on `aio_init`/`aio_free`.
- `Redirector/EventHandler.h` & `Redirector/EventHandler.cpp`: Instrument `tcpConnectRequest`, `udpSend`, and error points.
- `Redirector/TCPHandler.h` & `Redirector/TCPHandler.cpp`: Instrument `tcp_active`, `tcp_closed_total`, `redirect_success_total`, `redirect_failure_total`, `rx_bytes`, `tx_bytes`, `network_error_total` in `Handle`, `Read`, `Send`.
- `Redirector/DNSHandler.cpp`: Instrument `dns_query_total`, `dns_failure_total`, `rx_bytes`, `tx_bytes`, `network_error_total`.
- `Redirector/SocksHelper.cpp`: Instrument error counters on SOCKS handshake/associate failures.

### Managed Files to Modify / Add (`NekoProxyCore.*/`, `Netch/`)
- `Netch/Interops/Redirector.cs`: Declare `RedirectorStatistics` struct, P/Invoke `aio_getStats`, `aio_resetStats`.
- `NekoProxyCore.Core/Telemetry/INetFilterStatisticsProvider.cs` [NEW]: Provider interface.
- `NekoProxyCore.Core/Telemetry/CoreHealthSnapshotPayload.cs`: Add the 11 counter properties matching schema.
- `NekoProxyCore.Core/Telemetry/CoreTelemetryAggregator.cs`: Inject `INetFilterStatisticsProvider` to populate snapshot.
- `NekoProxyCore.Legacy/NetchProcessModeSessionResolver.cs`: Wire `INetFilterStatisticsProvider` to native interop.
- `NekoProxyCore.Host/Program.cs`: Wire statistics provider into runtime composition.

### Test Files to Add / Modify (`Tests/`, `Tests.Windows/`)
- `Tests/TelemetryMessageSerializationTests.cs`: Validate full snapshot JSON schema serialization.
- `Tests/CoreTelemetryAggregatorTests.cs`: Validate stats aggregation with mock provider.
- `Tests.Windows/RedirectorStatisticsInteropTests.cs` [NEW]: Validate struct memory layout and P/Invoke bridge against native DLL.

---

## 10. Execution Plan & Model Recommendation for T2B

- **Primary Team**: `TEAM_CORE`
- **Recommended Model**: `Gemini 3.7 Flash High`
- **Rationale**: The native hooks consist of straightforward atomic increments (`std::atomic<uint64_t>`) and a clean P/Invoke struct copy. There is no complex shared memory or custom lock-free queue required.
- **Escalation**: Not required. Claude remains reserved.
