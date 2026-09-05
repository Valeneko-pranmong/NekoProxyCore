# Changelog

All notable changes to NekoProxyCore are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

---

## 2026-09-05 — Neko Family 5.0 integration baseline

### Integrated Baseline
- **Commit Baseline**: `5c31cc5630ce58b8b72d536f1ec3860ba086fd27` (Merge pull request #2 from Valeneko-pranmong/integration/v5-core-main)
- **Source Parent**: `6ab94bb8fde80a5c675b39b6884c6d31db218dad`
- **Target Runtime**: .NET 6.0.428 (Windows x64)

### Added
- Complete integration of the stable Neko Core runtime architecture (`v5-core-main`).
- High-performance transparent proxy routing and lease lifecycle coordination engine.
- Streamlined ecosystem integration with Neko Launcher and Control Room management planes.
- Upstream legacy Netch attribution and reference documentation (`docs/reference/legacy-netch-upstream.md`).

### Changed
- Unified project documentation to reflect current active baseline and remove outdated `feature/neko-headless` and legacy branch references.
- Consolidated Windows platform redirection adapter and native driver interoperability boundaries.
- Refined security and lease lifecycle governance across platform service controllers.

### Fixed
- Stabilized cancelled lease regression during rapid session teardown and reconnection cycles (`6ab94bb`).
- Resolved race conditions in driver state cleanup on abrupt client termination.
- Synchronized Windows Filtering Platform (WFP) state disposal during abnormal service exit.

### Verification & Test Suite
- **Core Suite**: 342 / 342 tests passing (`Tests/Tests.csproj`)
- **Windows Platform Suite**: 94 / 94 tests passing (`Tests.Windows/Tests.Windows.csproj`)
- **Total Verification**: 436 / 436 tests passing
