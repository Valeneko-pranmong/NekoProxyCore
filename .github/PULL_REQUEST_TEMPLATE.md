## Summary of Changes

<!-- Provide a concise description of the purpose of this PR and what was changed. -->

## Type of Change

- [ ] Bug fix (non-breaking change fixing an issue)
- [ ] New feature (non-breaking change adding functionality)
- [ ] Refactor / Performance optimization
- [ ] Documentation update
- [ ] Platform / Driver interop update

## Component Scope

- [ ] `NekoProxyCore.Core`
- [ ] `NekoProxyCore.Host`
- [ ] `NekoProxyCore.Windows`
- [ ] `NekoProxyCore.Legacy`
- [ ] `Netch`

## Testing & Verification

Environment: Windows x64 | .NET 6.0.428

- [ ] `Tests/Tests.csproj`: Executed and passing (342/342 baseline)
- [ ] `Tests.Windows/Tests.Windows.csproj`: Executed and passing (94/94 baseline)
- [ ] Native Redirector prerequisite verified (`Redirector/bin/Release/Redirector.bin` and `nfapi.dll`)
- [ ] New regression/unit tests added covering new functionality

**Verification Output / Notes:**
<!-- Paste test summary or command output here -->

## Security & Lease Governance Checklist

- [ ] No secrets, private keys, authentication tokens, or wire protocol dumps are committed or logged.
- [ ] Lease management and session teardown lifecycle remain deterministic.
- [ ] Privilege boundaries between Core and Windows platform services are strictly respected.
- [ ] No unvalidated inputs introduced into IPC or control channels.

## Ecosystem & Upstream Compliance

- [ ] Verified compatibility with linked ecosystem components (`Neko Launcher`, `Control Room`).
- [ ] Retained GPLv3 licensing and upstream Netch attribution where applicable (`docs/reference/legacy-netch-upstream.md`).
- [ ] All new and modified files adhere to project coding guidelines.
