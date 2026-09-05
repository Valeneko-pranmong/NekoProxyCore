# NekoProxyCore CI triage

> **ARCHIVED STATIC PROPOSAL:** เอกสารนี้ตรวจ snapshot/queue ณ เวลาที่จัดทำและไม่มี
> active CI run รองรับ ห้ามใช้เป็นสถานะ CI ปัจจุบัน ดู
> [`../../current/core-release-handoff.md`](../../current/core-release-handoff.md)

Repository: `Valeneko-pranmong/NekoProxyCore`

## Current queue

- Open pull requests: 0
- Open issues: 0
- Pull-request workflow runs found for the latest commit: 0
- Latest repository commit observed: `9d99eb1c5a2acbf2a34f2600f94242601019a300` (2023-02-10)

There is therefore no active pull request check or failing job log to diagnose. The attached patch is a static CI-maintenance proposal, not a log-confirmed fix for a current PR.

## Findings addressed by the patch

1. `actions/upload-artifact@v3` is retired on GitHub.com and causes workflow failure. The patch moves the build workflow to `actions/upload-artifact@v7`.
2. The release workflow invokes `msbuild` through `build.ps1` but does not run `microsoft/setup-msbuild`. The patch adds the missing setup step.
3. The release workflow writes `NETCH_SHA256` but renders `env.Netch_SHA256`. The patch fixes the case mismatch.
4. First-party JavaScript actions are upgraded to current Node 24-compatible majors: checkout v7, cache v5, setup-go v6, and stale v10.
5. The archived `actions-rs/toolchain` action is replaced with `dtolnay/rust-toolchain@nightly`.
6. The old file-existence action is replaced with a small PowerShell check using `$GITHUB_OUTPUT`.
7. Workflow permissions are made explicit: read-only for builds and `contents: write` only for tagged releases.

## Verification performed

- Reviewed the three workflow files and `build.ps1` from the repository default branch.
- Confirmed the patch only changes workflow configuration.
- Parsed the patched workflow YAML locally.

## Verification still required

- Apply the patch to a checkout and run the build workflow on a branch or draft PR.
- Confirm the native MSYS2/Rust build remains compatible with current stable Go and nightly Rust.
- Confirm the release workflow on a non-production test tag before publishing a real release.
