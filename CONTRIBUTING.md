# Contributing to NekoProxyCore

Thank you for contributing to NekoProxyCore. Please review this guide before submitting issues, pull requests, or architectural changes.

---

## Code of Conduct & Core Rules

1. **Safety & Secrets**: Never commit, post, or distribute secrets, test credentials, tokens, protocol wire dumps, or private keys.
2. **Licensing**: All contributions must comply with the GNU General Public License v3.0 (GPLv3). Inherited code from Netch must retain appropriate notices in accordance with `docs/reference/legacy-netch-upstream.md`.
3. **Architectural Separation**:
   - `NekoProxyCore.Core` focuses on core runtime logic, communication and telemetry contracts, and authorization/permit verification.
   - `NekoProxyCore.Windows` provides Windows platform integration, process-level redirection integration, and platform-specific service resolution.
   - Ecosystem integration must align with Neko Launcher and Control Room communication contracts.

---

## Development Environment

- **Operating System**: Windows 10/11 x64 or Windows Server 2019/2022 x64
- **Toolchain**: .NET 6.0 SDK (specifically targeting .NET 6.0.428)
- **Native Components**: Visual Studio 2022 C++ build tools (when working on native driver interfaces)
- **Native Redirector**: Windows platform tests require pre-built native redirector dependencies (`Redirector/bin/Release/Redirector.bin` and `nfapi.dll`).

---

## Local Verification Workflow

Before submitting a Pull Request, verify that all test suites pass cleanly locally:

```bash
# 1. Build the managed Host project
dotnet build NekoProxyCore.Host/NekoProxyCore.Host.csproj -c Release --nologo

# 2. Run Core unit and integration tests (342 tests)
dotnet test Tests/Tests.csproj -c Release --nologo

# 3. Run Windows Platform tests (94 tests)
# Note: Requires native Redirector prerequisite (Redirector/bin/Release/Redirector.bin and nfapi.dll)
dotnet test Tests.Windows/Tests.Windows.csproj -c Release --nologo
```

Current test baseline requires **100% pass rate** (342/342 Core + 94/94 Windows).

---

## Branching & Pull Request Process

1. **Branch Baseline**: Base your feature or fix branch from `main` (integrated baseline `5c31cc5630ce58b8b72d536f1ec3860ba086fd27` or latest HEAD).
2. **Branch Naming**:
   - `feature/<description>`
   - `fix/<description>`
   - `docs/<description>`
   - `refactor/<description>`
3. **Commit Messages**:
   - Use imperative present tense (e.g., `fix: resolve lease cancellation deadlock`).
   - Clearly explain the reason for the change and any ecosystem impacts.
4. **Pull Request Submissions**:
   - Complete all sections of `.github/PULL_REQUEST_TEMPLATE.md`.
   - Include test results and notes on any driver or native prerequisites.
   - Ensure no regressions against the 436 verified automated tests.
