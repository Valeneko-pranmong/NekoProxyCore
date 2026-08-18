# NekoProxyCore Documentation Index

Last reviewed: **18 August 2026** (Phase PRE-T10 Repository Hygiene)

This is the canonical index for NekoProxyCore documentation. Documents are strictly separated by lifecycle to eliminate ambiguity between active source contracts, runtime behavior, and historical evidence.

---

## 1. Branch Roles & Authority Hierarchy

| Branch | Role / Classification | Commit Authority |
| :--- | :--- | :--- |
| **`feature/neko-auth-lite-v1-core`** | **Current Source-Development Authority** | `f269627351fc6a2c13b07c90f0e43ff69d17f058` |
| **`feature/neko-headless`** | Historical S0 Development Line | `3eb77e3` / `b3c9d0851cff74691500c431c0da1ec30c21927a` |
| **`main`** | Legacy / Upstream Reference Line | `9d99eb1` |
| **Production Artifact Authority** | **`AMBIGUOUS / NOT YET REFROZEN`** | (Historical S0: `b3c9d08`, V2Ray fix: `c3e3fb0`, Source: `f269627`) |

---

## 2. Active Core Documentation (`docs/current/`)

| Document | Classification | Purpose |
| :--- | :--- | :--- |
| **[`current/core-release-handoff.md`](current/core-release-handoff.md)** | `CURRENT_STATUS` | Core source release status, provenance distinction, and verification commands |
| **[`current/neko-auth-lite-core-contract.md`](current/neko-auth-lite-core-contract.md)** | `CURRENT_CONTRACT` | NEKO-AUTH-LITE (lite-v1) Core permit, challenge, and launch-boundary contract |
| **[`current/v2ray-runtime-fix-handoff.md`](current/v2ray-runtime-fix-handoff.md)** | `CURRENT_RUNTIME_AUTHORITY` | Verified V2Ray child process stdin invocation fix (`run -format=json`) |
| **[`current/protected-runtime-settings.md`](current/protected-runtime-settings.md)** | `CURRENT_CONTRACT` | Protected runtime configuration encryption and ephemeral memory handling |
| **[`current/runtime-config-discovery-contract.md`](current/runtime-config-discovery-contract.md)** | `CURRENT_CONTRACT` | Safe Core runtime configuration discovery protocol |

---

## 3. Architecture Specifications (`docs/architecture/`)

| Document | Classification | Purpose |
| :--- | :--- | :--- |
| **[`architecture/core-telemetry-contract.md`](architecture/core-telemetry-contract.md)** | `CURRENT_ARCHITECTURE` | Named Pipe `\\.\pipe\NekoProxyCoreTelemetry` JSON wire schema and metrics |
| **[`architecture/client-observability-privacy-boundary.md`](architecture/client-observability-privacy-boundary.md)** | `CURRENT_ARCHITECTURE` | Strict client-side observability privacy, forbidden fields, and aggregation rules |
| **[`architecture/netfilter-statistics-instrumentation.md`](architecture/netfilter-statistics-instrumentation.md)** | `CURRENT_ARCHITECTURE` | NetFilter packet driver statistics data plane and aggregator architecture |

---

## 4. Reference (`docs/reference/`)

| Document | Purpose |
| :--- | :--- |
| **[`reference/legacy-netch-upstream.md`](reference/legacy-netch-upstream.md)** | Original upstream Netch reference documentation |

---

## 5. Historical Archive (`docs/archive/`)

| Archive Topic | Path | Contents |
| :--- | :--- | :--- |
| **Telemetry** | [`archive/telemetry/`](archive/telemetry/) | [`core-telemetry-implementation-handoff.md`](archive/telemetry/core-telemetry-implementation-handoff.md), [`core-telemetry-monitoring-brief.md`](archive/telemetry/core-telemetry-monitoring-brief.md) |
| **Security S0** | [`archive/security-s0/`](archive/security-s0/) | Historical S0 authorization proposals, freeze requests, and test matrices |
| **Step D** | [`archive/step-d/`](archive/step-d/) | ProcessMode integration checkpoints prior to authorization |
| **Step E** | [`archive/step-e/`](archive/step-e/) | Headless host and Launcher boundary plan snapshots |
| **Plans & CI** | [`archive/plans/`](archive/plans/), [`archive/ci/`](archive/ci/) | Superseded build plans and CI maintenance proposals |
