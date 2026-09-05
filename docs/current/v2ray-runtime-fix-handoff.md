# NekoProxyCore — V2Ray Runtime Fix & Data Plane Handoff

- **Status:** `CLOSED`
- **Primary Team:** `TEAM_COORDINATION`
- **Source Owner:** `TEAM_CORE`
- **Packaging Owner:** `TEAM_LAUNCHER`
- **Team Web:** `NO ACTION`
- **Core Fix Commit:** `c3e3fb09ce20de7f05c34bb99cc77f7ebbebc710` (pre-rebase local: `3954b0fa03c5188bfdd7faea7b0fe30ba4d9fe89`)
- **Canonical Debug EXE SHA-256:** `6463329C0345E4C84A615791F7954ADFB31BBEBA99F43AB8D79B05E0E5CC2F63`

---

## 1. Historical Issue

During end-to-end integration with the real PSO2 game client, the proxy core data plane failed to route traffic to the remote proxy destination, resulting in PSO2 displaying "Ship Status: Unknown" and failing ship connection.

Investigation revealed two distinct issues that must not be collapsed:
1. **Packaging / Stale EXE (Historical Intermediate Defect):** An intermediate debug build omitted the bundled `bin\v2ray-sn.exe` child binary from packaging. After packaging provenance was repaired, a fresh package exposed the actual underlying Core defect.
2. **Core Child Process Invocation Defect:** When `V2rayController` attempted to launch `v2ray-sn.exe` using standard input streaming, the process failed immediately on Windows with `failed to load stdin:: The filename, directory name, or volume label syntax is incorrect.`

---

## 2. Technical History & Failure Progression

The failure isolation and resolution proceeded through the following verified 8-step progression:

1. **Historical RC used synthetic SOCKS5 runtime settings:** Early local development used local synthetic SOCKS5 settings rather than the production multi-hop pipeline.
2. **Real Shadowsocks authority was recovered:** Production credentials and topology were securely provisioned via `runtime-settings.nkps`.
3. **Real proxy architecture confirmed:**
   ```text
   pso2.exe
     → NetFilter SDK driver / Redirector.bin
     → Local SOCKS5 listener (127.0.0.1:2801)
     → v2ray-sn.exe (child process managed by V2rayController / Guard)
     → Remote Shadowsocks server (18.178.140.8:8388)
     → PSO2 JP Game Servers
   ```
4. **Stale Debug EXE discovery:** Discovered an older build that lacked `v2ray-sn.exe` in its staged payload.
5. **Packaging provenance repaired:** Launcher spec and staging directories were aligned with the canonical Core build artifact.
6. **Core source defect exposed:** With `v2ray-sn.exe` present, `V2rayController` invoked `v2ray-sn.exe run -c stdin:`. On Windows, the bundled `v2ray-sn.exe` (SagerNet / v2ray-core v5) treated `stdin:` as a literal Windows filesystem path, throwing `failed to load stdin: The filename, directory name, or volume label syntax is incorrect.`
7. **Minimal Core fix implemented:** Changed invocation argument from `run -c stdin:` to `run -format=json`.
8. **Real PSO2 live validation passed:** End-to-end game traffic routing proven with live ship selection and character selection.

---

## 3. Verified Root Cause

- **Root Cause Classification:** `V2RAY_WINDOWS_STDIN_INVOCATION_ARGUMENT_WAS_INVALID`
- **Root Cause Confidence:** `DEFINITIVE`
- **Old Argument:** `run -c stdin:`
- **Fixed Argument:** `run -format=json`

In SagerNet/V2Ray v5 on Windows, passing standard input JSON configuration requires `run -format=json` (which defaults to reading standard input when no file `-c` is passed). Passing `-c stdin:` attempts to treat `stdin:` as a Windows drive/file path.

---

## 4. Implemented Fix

### 4.1 Product Modification
In [`Netch/Servers/V2ray/V2rayController.cs`](../../Netch/Servers/V2ray/V2rayController.cs):
```diff
- await StartGuardWithStandardInputAsync("run -c stdin:", config);
+ await StartGuardWithStandardInputAsync("run -format=json", config);
```

### 4.2 Test Regression Guard
In [`Tests/LegacyRuntimeBootstrapContractTests.cs`](../../Tests/LegacyRuntimeBootstrapContractTests.cs):
```csharp
[TestMethod]
public void SocksProductionPathStreamsChildConfigurationWithoutPlaintextTempFile()
{
    var repositoryRoot = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(repositoryRoot, "Netch", "Servers", "V2ray", "V2rayController.cs"));

    Assert.IsFalse(source.Contains("Constants.TempConfig", StringComparison.Ordinal));
    Assert.IsFalse(source.Contains("FileStream", StringComparison.Ordinal));
    Assert.IsTrue(source.Contains("StartGuardWithStandardInputAsync", StringComparison.Ordinal));
    Assert.IsTrue(source.Contains("\"run -format=json\"", StringComparison.Ordinal));
}
```

---

## 5. Source Authority

- **Pre-Commit Core HEAD:** `d909a2a0f1a06562b060535ae57bb4d0cddcb251`
- **Core Fix Commit (Canonical Origin):** `c3e3fb09ce20de7f05c34bb99cc77f7ebbebc710`
- **Pre-Rebase Local Commit (Identical Patch):** `3954b0fa03c5188bfdd7faea7b0fe30ba4d9fe89`
- **Commit Message:** `pass` (origin) / `fix(core): use supported v2ray stdin config invocation`
- **Core Worktree Status:** Clean (`CORE_WORKTREE_CLEAN = YES`)
- **Launcher Source Modifications:** `NO` (`LAUNCHER_SOURCE_CHANGE = NO`)
- **Web Source Modifications:** `NO` (`WEB_SOURCE_CHANGE = NO`)
- **Push Performed:** `NO` (local commit only; awaiting explicit Owner instruction)

---

## 6. Artifact Authority

The canonical build and packaging artifacts for this verified release state:

| Asset | Path | SHA-256 |
|---|---|---|
| Canonical Core DLL | `C:\Temp\NekoProxyCore-Publish-Canonical\NekoProxyCore.dll` | `15306A4E6FEBD9C4545475ECDF6388A00972096DA5C24D1C4A4616952C297406` |
| Canonical V2Ray binary | `C:\Temp\NekoProxyCore-Publish-Canonical\bin\v2ray-sn.exe` | `A219F435671FB214C0C530084C65E576FDC1404F40B187B5586E869D2A3E4DFF` |
| Canonical Redirector | `C:\Temp\NekoProxyCore-Publish-Canonical\bin\Redirector.bin` | `EF325B06656B68302ED90B7C76877A845DF62C44182B59100D32E612CF7F514B` |
| Canonical Settings Payload | `C:\Temp\NekoProxyCore-Publish-Canonical\runtime-settings.nkps` | `BC82CDE38FB5BC8992D22311E1C7FDC63067D6202865600FC04E1167370D4689` |
| Final Packaged Debug EXE | `C:\Temp\neko-v2ray-canonical-rc3\dist\NekoLauncher-Debug.exe` | `6463329C0345E4C84A615791F7954ADFB31BBEBA99F43AB8D79B05E0E5CC2F63` |

- **Runtime Extraction Chain (`_MEI`):** `PASS` (verified exact SHA-256 matches across `_MEI188722`)
- **Plaintext Settings Present:** `NO`
- **Standalone Settings Key Present:** `NO`

---

## 7. Automated Test Results

Full automated test suites executed against committed source:

- **Core Unit Tests (`Tests/Tests.csproj`):** `PASS` (217 passed, 0 failed)
- **Core Windows Tests (`Tests.Windows/Tests.Windows.csproj`):** `PASS` (67 passed, 0 failed)
- **V2Ray Argument Regression Test:** `PASS`

---

## 8. Real-Runtime Proof (Live PSO2 Session)

Live runtime validation executed using the newly built canonical debug executable:

```text
AUTH_STATUS                    = AUTHENTICATED
CORE_STATUS                    = RUNNING
V2RAY_PROCESS_SEEN             = YES (PID 18760)
LOCAL_SOCKS_2801_LISTENING     = YES (127.0.0.1:2801 LISTEN)
REDIRECTOR_LOCAL_ESTABLISHED   = YES (127.0.0.1:2801 ESTABLISHED)
SS_REMOTE_ESTABLISHED          = YES (18.178.140.8:8388 ESTABLISHED)
SHIP_LIST_STATUS               = NORMAL (All 10 Ships Normal)
SHIP_SELECTION                 = PASS (Ship04 / Ship09)
CHARACTER_SELECT               = PASS
REAL_PSO2_NETWORK_PROXY_PROVEN = YES
```

---

## 9. Security & Process Notes

- **Launcher Auth / Security:** No changes made or required for this fix.
- **Web Backend:** No changes made or required.
- **Remote Infrastructure:** No server configuration changes, VPS reconfiguration, or port modifications required. Existing Shadowsocks server authority preserved.
- **Secret Protection:** No credentials, plaintext settings, or decryption keys written to source control or documentation (`SECRETS_WRITTEN_TO_DOCS = NO`).
- **Historical Records:** All previous incident reports and audit traces are preserved.

---

## 10. Current Team Status & Next Actions

| Team | Status | Responsibility |
|---|---|---|
| **TEAM_CORE** | `V2RAY FIX CLOSED` / `DATA PLANE PASS` | No further source changes required for this defect. |
| **TEAM_LAUNCHER** | `PACKAGING & INTEGRATION PASS` | No source changes required. Verified against canonical Core bundle. |
| **TEAM_WEB** | `NO ACTION` | No action required. |
| **TEAM_COORDINATION** | `DOCUMENTATION & HANDOFF COMPLETE` | Handoff authority established. |

### Notice for Next Engineer / Agent:
Do **NOT** re-debug or re-investigate:
- V2Ray stdin invocation arguments (`run -format=json` is definitive and proven).
- Proxy chain architecture (`Redirector → 127.0.0.1:2801 → v2ray-sn.exe → Shadowsocks`).
- Core ↔ Launcher named pipe authorization flow.
- Ship Status Unknown for this defect path.

### Production packaging requirement

`bin/v2ray-sn.exe` is a mandatory ProcessMode runtime dependency. Attempt
`DBG-f3608b` proved that omitting it from the external bundle and manifest fails
at `SOCKS_BOOTSTRAP` before Redirector/NetFilter initialization. Production
publishes therefore hash-pin the approved child executable, fail closed when it
is missing or mismatched, and include it in `core-manifest.json`.

### Next Action:
```text
NEXT_ACTION = CONTINUE WITH NEXT RELEASE / PROJECT GATE
```
