# Project Context — NekoProxyCore

Updated: 2026-09-12

## Purpose
NekoProxyCore is the low-level Windows Core/driver/runtime layer used by Neko Family Proxy. It owns local fail-closed authorization enforcement, process/network redirection integration, native redirector components and the headless Core host.

## Canonical repository state
- Repository: `Valeneko-pranmong/NekoProxyCore`
- Canonical branch: `main`
- Canonical `main` advances independently from the accepted v5.1.0 stable authority; verify live `origin/main` before work.

## Accepted v5.1.0 source authority
The currently accepted released Core source is intentionally preserved separately from canonical `main`:
- worktree: `E:\Github\worktrees\NekoProxyCore-release-5.1-stable`
- branch: `fix/stable-5.1-core-start-cancel-race-tdd4`
- SHA: `8705c7be2399c8624ae430f0c78543bddc6b953c`

Do not assume `origin/main` equals the source used by the accepted v5.1.0 Core artifact.

## Component map
- `NekoProxyCore.Core\` — runtime contracts, authorization and coordination.
- `NekoProxyCore.Host\` — headless executable/control host.
- `NekoProxyCore.Windows\` — Windows process/runtime integration.
- `NekoProxyCore.Legacy\` and `Netch\` — inherited network-engine adapter/runtime.
- `Redirector\`, `RouteHelper\` — native redirection components.
- `Tests\`, `Tests.Windows\` — managed and Windows-specific verification.

## Security boundary
Core must fail closed on invalid/missing authorization. Do not log or persist trust material, proxy secrets or secret-bearing runtime configuration casually. Core receives authorized start configuration through the Launcher path; the Admin web is not a direct Core controller.

## Build/test baseline
The documented SDK baseline is .NET 6.0.428. Full Windows/native verification needs the Redirector/native artifacts and Windows build toolchain. Check `global.json`, README and project files before changing tool versions.

## Related projects
- `E:\Github\Neko-Family-Proxy` — Launcher/client and release/update controller.
- `E:\Github\Neko-Family-Proxy-admin-tool` — operator web control plane.
- `E:\Github\Neko-Core AWS` — production server/infrastructure.
- `E:\Github\Project manager` — cross-project authority/handoff.

## Read next
1. `README.md`
2. `HANDOFF.md`
3. `E:\Github\Project manager\projects\CORE.md`
4. accepted stable worktree when investigating the released v5.1.0 Core.
