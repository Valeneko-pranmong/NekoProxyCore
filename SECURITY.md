# Security Policy

## Supported Versions

Security fixes are actively maintained and backported to the current integrated baseline on the `main` branch.

| Version / Baseline | Status | Supported |
| :--- | :--- | :--- |
| `main` (`5c31cc5`+) | Active Production Baseline | Yes |
| Pre-integration / Legacy Branches | Deprecated | No |

---

## Security Architecture & Core Principles

NekoProxyCore operates at the core network routing and system redirection layer. We enforce several critical security principles across all codebase layers:

1. **Privilege Boundary Integrity**: Elevated operations (such as WFP firewall rules, routing table manipulation, and native driver control) are strictly bounded to the Windows platform subsystem.
2. **Lease Management & Safe Teardown**: Interception state is maintained under strict lease mechanisms. If client processes, the CLI host, or orchestrators crash, leases expire deterministically, restoring original system network configuration without leaving open or dangling proxy traps.
3. **Protection of Trust Material**:
   - Never commit, log, or export private keys, TLS/mTLS credentials, proxy authentication tokens, or sensitive payload data.
   - Secrets and wire-level protocol details must remain compartmentalized.
   - Diagnostic and telemetry logging must enforce strict data scrubbing.
4. **Input Validation & IPC Defense**: All IPC messages received from linked ecosystem components (Neko Launcher, Control Room) undergo rigorous validation prior to execution.

---

## Reporting a Vulnerability

We take the security of NekoProxyCore and our users seriously. If you discover a vulnerability or security-sensitive flaw, please report it through responsible disclosure channels:

- **Do NOT open a public GitHub issue** for undisclosed security vulnerabilities.
- Submit a report via GitHub Private Vulnerability Reporting or open a private Security Advisory if enabled on the repository.
- If private vulnerability reporting / advisories are not enabled, contact the repository owner or maintainers privately.
- Include the following details:
  - Description of the vulnerability and attack vector.
  - Affected components, commit baseline, and platform configuration.
  - Step-by-step reproduction instructions or a minimal proof of concept (PoC).
  - Impact assessment (e.g., privilege escalation, denial of service, traffic leakage).

### Vulnerability Handling

Reports are reviewed through private coordination channels to investigate the reported issue, coordinate necessary fixes, and prepare security advisories. We ask that you maintain confidentiality until an official patch or advisory has been published.
