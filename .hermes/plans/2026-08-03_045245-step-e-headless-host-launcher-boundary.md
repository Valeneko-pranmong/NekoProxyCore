# Step E Headless Host และ Launcher Boundary Implementation Plan

> **For Hermes:** ใช้แนวทาง TDD และดำเนินการทีละ task โดยตรวจ spec compliance และ code quality ก่อนข้าม checkpoint

**Goal:** สร้าง `NekoProxyCore.exe` แบบ `WinExe` ที่ไม่มี UI/console/tray ให้ NekoLauncher เริ่ม ProcessMode ได้เฉพาะเมื่อ Backend/Launcher อนุญาต session ที่ยัง active และตรวจพบ `pso2.exe` แล้ว, รับ/ส่งเฉพาะ typed + sanitized data ผ่าน IPC และหยุด/cleanup ได้แบบ bounded โดยไม่ทิ้ง child process หรือ state ค้าง

**Architecture:** เพิ่ม production host projectแยกจาก Netch WinForms. Backend เป็น authority ของ auth/entitlement/session และต้องออกหลักฐานอนุญาตเริ่มงานแบบ short-lived ที่ Core ตรวจสอบได้; Launcher เป็น policy/orchestration boundary ที่ขอหลักฐานดังกล่าวหลัง session/heartbeat ผ่าน. จากนั้น Launcher ตรวจพบ `pso2.exe` ก่อนสร้าง hostและส่งคำสั่ง `start`; ฝั่ง hostตรวจทั้งหลักฐานอนุญาตและ processซ้ำผ่าน `WindowsProcessResolver` ก่อนเริ่ม engine จึง fail closed คนละชั้น. IPC แบบ current-user-only เป็น transport isolation เท่านั้น ไม่ใช่ authorization เพราะโปรแกรมอื่นของ Windows user คนเดียวกันยังเรียกได้. Host compose `HeadlessRuntimeCoordinator → ProcessModeController → NetchProcessModeEngine` โดย reuse Step D contract และย้าย runtime bootstrapที่ซ้ำกับ integration runnerไปไว้หลัง non-UI seam.

**Tech Stack:** .NET 6 / C# (`WinExe`, `System.IO.Pipes`, JSON Lines), MSTest, Python 3 / `subprocess` + named-pipe client, pytest, Visual Studio MSBuild 17.14.51, Windows ProcessMode/netfilter2

---

## 1. สถานะและข้อสรุปจากการสำรวจ

- Branch ปัจจุบันคือ `feature/neko-headless`, HEAD `03ecb15`; worktree ของ NekoProxyCore สะอาด
- Launcher checkout พบที่ `D:\Neko-Family-Proxy`, branch `main`; worktree สะอาด
- Step D gate ผ่านแล้วตาม `tools/PROCESSMODE_TEST_REPORT.md`; ห้าม hard-code traffic PASS ใน Step E
- Core contract พร้อมใช้แล้วที่:
  - `NekoProxyCore.Core/RuntimeContracts.cs`
  - `NekoProxyCore.Core/HeadlessRuntimeCoordinator.cs`
  - `NekoProxyCore.Core/ProcessModeController.cs`
- Concrete composition ที่พิสูจน์แล้วอยู่ใน `NekoProxyCore.IntegrationRunner/Program.cs:39-49`
- Legacy resolver ยังอ่าน runtime state ผ่าน `Global.Settings`/`Global.Modes` ที่ `NekoProxyCore.Legacy/NetchProcessModeSessionResolver.cs:36-55`; host ต้อง bootstrap state โดยไม่เรียก Netch `Program.Main`, `Application.Run`, `Global.MainForm`, `MessageBoxX`, `NotifyIcon` หรือ `ModeService.Load()`
- Launcher ปัจจุบันยังเปิด `ProxyCore.exe` แบบ visible และถือว่า `Popen` สำเร็จเท่ากับ running ที่ `D:\Neko-Family-Proxy\launcher\src\neko_launcher\infrastructure\process_manager.py:20-39`
- Launcher มี process detector ที่จำกัดเฉพาะ `pso2.exe` แล้ว แต่ controller ยังสามารถรับ `StartProxyRequested` โดยไม่ตรวจ game-process state; Step E ต้องปิดช่องนี้ด้วย test แบบ fail closed

## 2. ขอบเขต Step E

### อยู่ใน scope

1. Production `NekoProxyCore.exe` แบบ `WinExe`, x64, framework-dependent สำหรับ development gate
2. Single-instance host ต่อ Windows user
3. Minimal IPC commands: `start`, `status`, `stop`
4. Typed status/error/correlation ID และ protocol version
5. Opaque identifiers เท่านั้น: `pso2.exe`, `profile-N`, `server-N`
6. Runtime bootstrap ที่ไม่แตะ UI path
7. Launcher adapter ที่รอ `pso2.exe` ก่อนเริ่ม host/ProcessMode
8. Graceful stop เมื่อ game ปิด, launcher ปิด, host ล้มเหลว หรือ IPC timeout
9. Unit/contract, headless smoke และ ProcessMode E2E evidence
10. Backend-issued, short-lived launch authorization ที่ผูกกับ active launcher session/installation และ Core ตรวจสอบก่อน start

### นอก scope

- PcapMode/TunMode
- เปลี่ยน `ProxyConfiguration` ให้รับ URI/credential/path
- package/installer, signing, clean-machine release approval
- DPAPI credential migration (เป็น Phase 5 แยกต่างหาก)
- แก้ full `Netch.sln` build blocker ด้วยการเปลี่ยน source C++/legacy
- ลบ `MainForm` ทั้งก้อนหรือ refactor UI ส่วนที่ ProcessMode host ไม่ได้เรียก

## 3. Authorization และ IPC contract ที่ต้อง freeze ก่อนเขียน host

### Authorization correction (security gate)

- `pso2.exe` detection เป็น activation precondition ไม่ใช่หลักฐานสิทธิ์ใช้งาน
- State ใน Python เช่น `AUTHENTICATED`, active entitlement และ `session_id` ป้องกันเฉพาะ normal UI flow; ผู้ใช้ที่แกะ/แก้ Launcher สามารถข้าม branch เหล่านี้ได้
- `PipeOptions.CurrentUserOnly` ป้องกัน cross-user access แต่ไม่ป้องกัน executable อื่นของ Windows user เดียวกันเรียก Core โดยตรง
- ห้ามใช้ `session_id`, boolean `authorized=true`, shared secretที่ฝังใน Launcher/Core, mutex, parent PID หรือ executable path เป็น authorization proof เพราะปลอม/replay/extract ได้
- ก่อน `start` Core ต้องตรวจหลักฐานที่ Backend ออกและ client forge ไม่ได้ เช่น signed short-lived launch permit ที่มีอย่างน้อย protocol/audience, session/installation binding, issued/expiry time และ unique nonce; Coreถือเฉพาะ public verification key
- Permit ต้องมีอายุสั้น, ใช้ซ้ำไม่ได้ภายใน host instance, ไม่ถูกเขียนลง argv/log/report/disk และถูกล้างจาก memoryตามสมควรหลัง validation
- Backend ยังคงเป็น authority: permit issuance ต้อง fail closed หาก auth, license, installation, claimed session หรือ heartbeat ไม่ valid
- Local anti-tamper ไม่สามารถรับประกัน 100% บนเครื่องที่ผู้ใช้ควบคุม; code signing, integrity checks และ packaging เป็น defense-in-depth/release gates ไม่ใช่สิ่งทดแทน server authorization

**Stop condition:** ห้ามเดิน production host/Launcher integration ต่อจน freeze วิธีออกและตรวจ launch permit รวมถึง online/offline policy, TTL, replay behavior และ key rotation.

ใช้ named pipe ต่อผู้ใช้ เช่น `NekoProxyCore.v1.<user-scope-hash>` โดย server สร้างด้วย `PipeOptions.CurrentUserOnly`. ห้ามรับชื่อ pipe, runtime root, profile path หรือ credential จาก argv.

แต่ละ message เป็น JSON object หนึ่งบรรทัด UTF-8 และมีขนาดสูงสุดที่กำหนด (แนะนำ 8 KiB) เพื่อป้องกัน unbounded input. Unknown field อนุญาตเพื่อ forward compatibility แต่ unknown command/protocol version ต้องคืน typed error.

### Request

```json
{"version":1,"command":"start","correlationId":"launcher-001","processName":"pso2.exe","profileReference":"profile-0","serverReference":"server-0","launchPermit":"<short-lived signed opaque permit>"}
{"version":1,"command":"status","correlationId":"launcher-002"}
{"version":1,"command":"stop","correlationId":"launcher-003"}
```

### Response/event

```json
{"version":1,"kind":"result","correlationId":"launcher-001","status":"Running","succeeded":true,"errorCode":null}
{"version":1,"kind":"status","correlationId":"launcher-001","status":"Failed","succeeded":false,"errorCode":"ProcessNotFound"}
```

กฎ contract:

- Wire protocol ไม่มี `message`, exception text, hostname, server remark, path, URI หรือ credential
- `launchPermit` เป็น authorization material: ห้าม log/echo/persist และ response ต้องไม่มี permit
- `errorCode` map จาก `ProxyErrorCode` เท่านั้น
- `correlationId` และ identifiers ต้องผ่าน validation เดิมของ Core
- `start` ระหว่าง Starting/Running คืน `AlreadyRunning`; `stop` ซ้ำคืน Stopped success
- Host ไม่เริ่ม runtime ตอน process boot; เริ่มเฉพาะเมื่อได้รับ valid authorized `start`
- Missing/expired/invalid/replayed permit ต้อง fail closed ก่อนสร้าง `ProxyConfiguration` และก่อนเรียก runtime/engine
- ทั้ง Launcher และ host ต้องตรวจ `pso2.exe`; หากไม่มี target ให้คืน failure และ engine start count ต้องเป็นศูนย์
- จำกัด one request at a time หรือ serialize ผ่าน lifecycle gate; response ทุกคำสั่งต้อง bounded timeout

## 4. แผนดำเนินงาน

### Task 0: Freeze Backend-issued launch authorization

**Objective:** ทำให้ Core ไม่สามารถถูกเปิดใช้งานโดยข้าม Launcher authorization flow เพียงด้วยการเรียก executable/pipe ตรง ๆ

**Cross-repository files (final paths depend on selected permit implementation):**
- Supabase migration/Edge Function สำหรับ issue short-lived launch permit
- Launcher gateway/service model สำหรับขอ permitหลัง active session validation
- Host verifier contract/implementationที่ถือเฉพาะ public verification material
- C#/Python/backend contract fixturesและ security tests

**Required tests:** unauthorized/missing permit, malformed signature, wrong audience/session/installation, expired/not-yet-valid permit, replay, revoked session, expired entitlement, stale heartbeat, key rotation, permit sentinelไม่ปรากฏใน argv/log/response/artifact. ทุก failure ต้องมี engine start count 0.

### Task 1: Freeze Step E wire contract ด้วย unit tests

**Objective:** กำหนด schema, validation, protocol version และ secret-safety ก่อนสร้าง pipe server

**Files:**
- Create: `NekoProxyCore.Host/NekoProxyCore.Host.csproj`
- Create: `NekoProxyCore.Host/Protocol/ControlRequest.cs`
- Create: `NekoProxyCore.Host/Protocol/ControlResponse.cs`
- Create: `NekoProxyCore.Host/Protocol/ControlProtocol.cs`
- Create: `Tests/HeadlessHostProtocolTests.cs`
- Modify: `Tests/Tests.csproj`

**Steps:**

1. เขียน failing tests สำหรับ authorized valid `start`, `status/stop`, missing/invalid/replayed permit, unsupported version/command, malformed JSON, oversized frame, invalid correlation/opaque refs และ secret-like input
2. ยืนยันว่า serialized response มีเฉพาะ allow-listed fields และไม่มี exception message
3. Implement serializer/parser แบบ bounded และ map `ProxyResult`/`ProxyStatusSnapshot` เป็น wire response
4. รัน:
   ```powershell
   dotnet test .\Tests\Tests.csproj -c Release --filter HeadlessHostProtocolTests
   ```
5. Acceptance: invalid input คืน `InvalidConfiguration` แบบ sanitized และไม่สร้าง `ProxyConfiguration`/ไม่เรียก runtime

### Task 2: แยก legacy runtime bootstrap ที่ไม่เรียก UI

**Objective:** ทำให้ production host โหลด settings/modes และ compose ProcessMode session โดยไม่ reuse console-oriented integration runner entry point

**Files:**
- Create: `NekoProxyCore.Legacy/NetchRuntimeBootstrap.cs`
- Modify: `NekoProxyCore.IntegrationRunner/Program.cs:22-34,126-143`
- Create: `Tests/LegacyRuntimeBootstrapContractTests.cs`

**Steps:**

1. เขียน test ตรวจ source/call boundary ว่า bootstrap ไม่อ้าง `Global.MainForm`, `Application`, `Form`, `MessageBox`, `NotifyIcon`, `ModeService.Load`, `Program.CreateLogger` หรือ console
2. ย้ายเฉพาะ logic ที่พิสูจน์แล้ว: set runtime working directory, append `bin` to PATH แบบ idempotent, create `logging`, `Configuration.LoadAsync`, clear/load `Global.Modes` ด้วย `ModeHelper.LoadMode`
3. ให้ bootstrap รับ runtime root จาก trusted host construction (`AppContext.BaseDirectory`) ไม่รับจาก IPC/argv
4. ปรับ integration runner ให้เรียก bootstrap เดียวกันเพื่อป้องกัน host/runner drift
5. รัน `LegacyRuntimeBootstrapContractTests` และ full `Tests.csproj`

**Stop condition:** ถ้าต้องเรียก `Global.MainForm` หรือ `ModeService.Load()` ให้หยุดและแยก seam ใหม่ ไม่ใช้ hidden form เป็นทางลัด

**Security checkpoint note:** ก่อน Task 0/authorization gate ผ่าน ให้ `NekoProxyCore.Host` เป็น protocol library เท่านั้น ห้ามมี runnable `WinExe` entry point ที่รับ unauthenticated start. การตั้ง `OutputType=WinExe`, RID และ x64 เป็นงาน Task 3/E2 หลัง authorization verifier พร้อม

### Task 3: สร้าง host composition root และ single-instance lease

**Objective:** สร้าง runtime process ที่ไม่เริ่ม proxy เอง, มี instance เดียวต่อ user และ cleanup deterministic

**Files:**
- Create: `NekoProxyCore.Host/HostComposition.cs`
- Create: `NekoProxyCore.Host/SingleInstanceLease.cs`
- Create: `NekoProxyCore.Host/Program.cs`
- Create: `Tests/HeadlessHostCompositionTests.cs`

**Composition:**

```text
NetchRuntimeBootstrap
  → NetchProcessModeSessionResolver
  → NetchProcessModeEngine
  → ProcessModeController(WindowsProcessResolver)
  → HeadlessRuntimeCoordinator
  → HeadlessControlServer
```

**Steps:**

1. เขียน tests ด้วย fake runtime เพื่อยืนยันว่า process boot ไม่เรียก `StartAsync`
2. เพิ่ม named mutex/lease ที่ชื่อไม่ชน Netch UI เดิม และไม่ใช้ package single-instance ที่ส่ง UI show message
3. instance ที่สองต้องคืน stable exit code และไม่รบกวน runtime ตัวแรก
4. ผูก process shutdown/cancellation กับ bounded `runtime.StopAsync`; ห้าม `Environment.Exit` ก่อน cleanup ปกติ
5. ห้ามใส่ `UseWindowsForms`; project ต้องเป็น `OutputType=WinExe`, `TargetFramework=net6.0-windows`, `RuntimeIdentifier=win-x64`, `PlatformTarget=x64`

### Task 4: Implement current-user named-pipe control server

**Objective:** เชื่อม IPC contract กับ `IProxyRuntime` โดยรักษา serialization, timeout และ sanitized status

**Files:**
- Create: `NekoProxyCore.Host/HeadlessControlServer.cs`
- Create: `NekoProxyCore.Host/RuntimeStatusBroadcaster.cs`
- Create: `Tests/HeadlessControlServerTests.cs`

**Steps:**

1. เขียน integration-style tests ด้วย fake runtime และ real named pipe ชั่วคราวสำหรับ start/status/stop
2. ทดสอบ client disconnect ระหว่าง start, partial/malformed frame, oversized input และ duplicate start
3. ใช้ `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`; จำกัด client และ serialize command processing
4. `start` ต้องสร้าง `ProxyConfiguration.TryCreate(...)` และเรียก coordinator เท่านั้นหลัง validation
5. ส่ง response/event ด้วย enum names + correlation ID เท่านั้น; sink failure/client disconnect ห้ามทำ lifecycle พัง
6. เมื่อ pipe server ยุติ ต้อง stop runtime แบบ bounded และ dispose pipe/mutex

### Task 5: Build `NekoProxyCore.exe` และทำ static/headless smoke gate

**Objective:** พิสูจน์ artifact จริงว่าไม่มี console/top-level window/tray และไม่มี UI callback ใน production call path

**Files:**
- Modify: `Netch.sln` (เพิ่ม managed host project เท่านั้น ถ้าไม่ทำให้ native solution gateสับสน; มิฉะนั้นบันทึก host project เป็น independent gate)
- Create: `tools/run-headless-host-smoke.ps1`
- Create: `Tests/HeadlessHostPackagingTests.cs`

**Steps:**

1. Build Core/Windows/Legacy targets ตาม handoff แล้ว publish host win-x64 โดยใช้ verified legacy artifacts pattern เดียวกับ integration runner
2. ตรวจ artifact มีชื่อ `NekoProxyCore.exe`, ไม่มี `NekoProxyCore.Host.exe`/console host ที่ไม่ตั้งใจ และ stage RID-specific Windows DLL ครบพร้อม hash verification
3. Smoke script เปิด host โดยไม่มี target/start request แล้วตรวจ:
   - process อยู่รอดและ proxy ยัง Stopped
   - ไม่มี top-level visible window
   - ไม่มี console window
   - ไม่มี Netch/NekoProxyCore tray icon หรือ notification
   - instance ที่สองไม่สร้าง runtime ซ้อน
   - ส่ง `status`, `stop`; mutex/pipe ถูกคืนและไม่มี child processค้าง
4. Static scan production host/Core path สำหรับ `System.Windows.Forms`, `Global.MainForm`, `MessageBox`, `NotifyIcon`, `Application.Run`, `AllocConsole`, `ShowWindow`, `BeginInvoke`, `Invoke(`
5. บันทึก artifact path และ SHA-256 แต่ระบุชัดว่าเป็น development artifact ไม่ใช่ release

### Task 6: เปลี่ยน Python Launcher gateway เป็น typed host client

**Objective:** ให้ Launcher start/stop/status ผ่าน IPC และไม่ถือว่า `Popen` สำเร็จเท่ากับ Running

**Files ใน `D:\Neko-Family-Proxy`:**
- Modify: `launcher/src/neko_launcher/infrastructure/process_manager.py`
- Create: `launcher/src/neko_launcher/infrastructure/proxy_control_client.py`
- Modify: `launcher/tests/test_process_manager.py`
- Create: `launcher/tests/test_proxy_control_client.py`

**Steps:**

1. เขียน failing tests ว่า command line มีเฉพาะ executable path ไม่มี profile/server/secret และใช้ `CREATE_NO_WINDOW`/WinExe โดยไม่ใช้ window-hiding API
2. เขียน fake-pipe tests สำหรับ typed start/status/stop, timeout, invalid response, host early exit และ correlation mismatch
3. `ProxyProcessManager.start()` ต้อง:
   - fail closed หาก detector ไม่พบ `pso2.exe`
   - เริ่มเฉพาะ trusted packaged `NekoProxyCore.exe`
   - รอ pipe readiness แบบ bounded
   - ส่ง opaque `start`
   - รอ typed `Running`; `Popen` อย่างเดียวไม่ถือว่าสำเร็จ
4. `stop()` ส่ง graceful `stop`, รอ Stopped/process exit แบบ bounded; force-kill ได้เฉพาะ host process tree ที่ Launcher เป็นผู้สร้าง หลัง timeout และต้องรายงาน failure
5. ห้ามอ่าน raw exception/error message จาก host ไปแสดง; map `errorCode` เป็น localized Launcher text ที่ presentation layer

### Task 7: บังคับ start-after-detection ใน Launcher controller

**Objective:** ปิดทุกทางที่อาจเริ่ม ProcessMode ก่อน `pso2.exe` แม้มี direct `StartProxyRequested`

**Files ใน `D:\Neko-Family-Proxy`:**
- Modify: `launcher/src/neko_launcher/application/controller.py:87-110,157-190`
- Modify: wiring จุดที่ publish `GameProcessStateChanged` (ค้นและยืนยันก่อนแก้)
- Modify: `launcher/tests/test_controller.py`
- Modify: `launcher/tests/test_process_detector.py`

**Steps:**

1. เพิ่ม failing test: authenticated + entitled + session active แต่ `game_process_running=False` ต้องไม่เรียก proxy gateway
2. เพิ่ม failing test: `GameProcessStateChanged(True)` เป็น trigger ที่อนุญาต start เพียงครั้งเดียว; repeated detection ไม่สร้าง hostซ้อน
3. เพิ่ม failing test: detection error/timeout = not detected และไม่ start
4. เพิ่ม failing test: `pso2_bin.exe` ไม่ถือเป็น target เว้นแต่ product requirement เปลี่ยนอย่างชัดเจน; current source of truth คือ `pso2.exe` เท่านั้น
5. เมื่อ `GameProcessStateChanged(False)`, ให้ coordinator/host stop ตัวเองจาก process watcher และ Launcher ส่ง stop แบบ idempotentซ้ำได้
6. รักษา UI/system tray ownership ใน Launcher; network thread/IPC callback ต้อง publish event ไม่เรียก Tk methodตรง ๆ

### Task 8: Cross-repository contract และ failure-path tests

**Objective:** ยืนยันว่า C# host และ Python client เข้าใจ schema/exit behavior ตรงกันโดยไม่พึ่ง credential/runtime จริง

**Files:**
- Create: `tools/step-e-contract-fixtures/` เฉพาะ sanitized JSON fixtures หรือ generated schema ที่ไม่มี settings จริง
- Create/Modify: C# protocol tests และ Python client testsให้ consume fixtures เดียวกัน
- Modify: handoff docs หลังผลจริงเท่านั้น

**Cases:**

- valid start → Starting → Running
- process absent → `ProcessNotFound`/typed failure และ engine start count 0
- process exits during start → `ProcessExited`
- duplicate start → `AlreadyRunning`
- stop repeated → Stopped success
- start/stop timeout and cancellation
- host crash / malformed response / pipe disconnect
- launcher exit during Running → graceful cleanup then owned-process termination fallback
- secret sentinel in request/exception → ไม่ปรากฏใน response/log/test artifact
- second launcher/host instance → ไม่สร้าง proxy sessionซ้อน

### Task 9: Step E real integration gate

**Objective:** ยืนยัน production host + real Launcher boundary กับ `pso2.exe` และ ProcessMode path เดิม

**Preconditions:**

- รัน preflight ใหม่และ `FAIL=0`
- Core/Windows tests ผ่าน; Legacy ทั้ง targets build ผ่านด้วย VS MSBuild environment ที่ระบุ
- ใช้เฉพาะ `Original setting/`/runtime input ที่ approved และ ignored; ห้าม stage/archive
- ผู้ใช้เปิดเกมจนตรวจพบ `pso2.exe` ก่อน host start

**Test flow:**

1. เปิด Launcher/Tweaker ขณะยังไม่มี `pso2.exe`; ยืนยันไม่มี `NekoProxyCore.exe`, v2ray child หรือ proxy start
2. ให้ `pso2.exe` ปรากฏ; ยืนยัน Launcher เริ่ม `NekoProxyCore.exe` หนึ่งตัวและรับ typed Running
3. ยืนยันไม่มี core window, console, tray, balloon หรือ Netch UI
4. ทำ local SOCKS readiness + gameplay traffic window แบบเดียวกับ Step D
5. ยืนยัน target traffic ด้วย server-side Shadowsocks TCP/UDP counters โดยไม่เก็บ payload/credential
6. ปิด `pso2.exe`; ยืนยัน Stopping → Stopped, core/child process exit, pipe/mutex/controller cleanup
7. ทดสอบ Launcher shutdown และ host crash อย่างละหนึ่งรอบ; Launcher ต้องไม่ zombie และเปิดใหม่ได้
8. ตรวจ `%TEMP%`, process tree, `v2ray-sn.exe`, service/controller state และ log ว่าไม่มี orphan/secret
9. บันทึก PASS/FAIL/BLOCKED จาก output จริง; ห้ามสร้าง PASS generator หรือเปลี่ยน static Step D traffic gate

## 5. Verification commands

### NekoProxyCore repository

```powershell
Set-Location D:\NekoProxyCore
git status -sb
git merge-base --is-ancestor 99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687 HEAD
& .\tools\neko-proxycore-preflight.ps1

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnet restore .\Tests\Tests.csproj
& $dotnet build .\NekoProxyCore.Core\NekoProxyCore.Core.csproj -c Release --no-restore
& $dotnet build .\NekoProxyCore.Windows\NekoProxyCore.Windows.csproj -c Release --no-restore
& $dotnet test .\Tests\Tests.csproj -c Release --no-restore
```

Legacy build ใช้ x64 VS Developer environment:

```bat
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\6.0.428\Sdks
set MSBuildEnableWorkloadResolver=false
msbuild.exe D:\NekoProxyCore\NekoProxyCore.Legacy\NekoProxyCore.Legacy.csproj /m /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
```

Host publish/smoke command ให้เพิ่มใน Task 5 และต้องคืน non-zero เมื่อ window/console/duplicate instance/cleanup gate ใดไม่ผ่าน

### Launcher repository

```powershell
Set-Location D:\Neko-Family-Proxy\launcher
python -m ruff check src tests
python -m pytest -q
python -m compileall -q src
```

ก่อนแก้ Launcher ให้ตรวจ environment/คำสั่งจริงจาก repo docs และ manifest อีกครั้ง; ห้ามถือ command ในแผนแทนผล execution

## 6. Acceptance criteria ของ Step E development

- [ ] `NekoProxyCore.exe` เป็น `WinExe` x64 และ process boot ไม่เริ่ม proxyเอง
- [ ] ไม่มี visible window, console, tray, balloon หรือ Netch UI
- [ ] ไม่มี `Global.MainForm`/UI callback ใน production host ProcessMode call path
- [ ] Host/Launcher start ProcessMode ได้เฉพาะหลัง `pso2.exe` detected; absence/error fail closed
- [ ] Host ตรวจ target processซ้ำก่อน engine start
- [ ] IPC current-user-only, bounded, versioned และรับเฉพาะ opaque identifiers
- [ ] ไม่มี credential/URI/path/exception detail ใน argv, IPC response, log หรือ artifact
- [ ] Launcher ใช้ typed Running ไม่ใช้ `Popen` เป็น readiness signal
- [ ] start/stop/status, duplicate start/stop, timeout, cancellation, process exit และ crash cleanup ผ่าน
- [ ] Real Launcher → host → ProcessMode → PSO2 gameplay path ผ่าน พร้อม external TCP/UDP counter evidence
- [ ] ปิดเกม/Launcher แล้วไม่มี orphan core, integration runner, `v2ray-sn.exe`, pipe, mutex หรือ controller state
- [ ] Launcher UI/tray ยังทำงานและเปิดใหม่ได้โดยไม่ restart Windows
- [ ] Handoff/report ถูก sanitize และแยก development PASS จาก package/signing/clean-machine release gates

## 7. ความเสี่ยงและ stop conditions

| ความเสี่ยง | วิธีรับมือ |
|---|---|
| `Global` static state ยังอยู่ใน Netch assembly | จำกัดและทดสอบ production call path; ห้ามเรียก property `Global.MainForm`; แยก bootstrap ก่อน ไม่ rewrite Netch ทั้งก้อน |
| Named pipe ถูก process อื่นเรียก | `CurrentUserOnly`, stable protocol validation, bounded frames; หากสิทธิ์ไม่พิสูจน์ได้ให้ gate เป็น FAIL |
| Legacy log หลุด secret | ไม่ส่ง log ผ่าน IPC, ใช้ allow-listed wire fields, sentinel redaction tests และ scan artifact/log |
| Launcher start race ก่อน target | detector gate ฝั่ง Launcher + resolver gateฝั่ง host; engine start count 0 เมื่อ target absent |
| Host/Launcher protocol drift | shared sanitized fixtures + protocol version + cross-repo tests |
| Force kill ทิ้ง driver/controller state | graceful stop ก่อนเสมอ; force only owned process tree หลัง timeout และ classify FAIL |
| Host publish ดึง Windows/WinForms dependency | แยก “assembly dependency” ออกจาก “UI invocation”; smoke/static call-path gate ต้องพิสูจน์ว่าไม่มี UI ถูกสร้างจริง |
| Scope ขยายไป package/release | หยุดหลัง Step E development/E2E; signing, installer, notices, clean machine เป็น downstream gates |

**Hard stop:** หาก implementation ต้องส่ง secret ผ่าน argv/IPC/log, เปิด Form/MessageBox/tray/console, start ก่อนพบ `pso2.exe`, ใช้ polling watcherถาวร, หรือแก้ Pcap/TUN เพื่อให้ build ผ่าน ให้หยุดและ redesign seam ก่อนทำต่อ

## 8. Suggested checkpoints

1. **E1 — Protocol + bootstrap:** Tasks 1-2, contract testsผ่าน
2. **E2 — Host:** Tasks 3-5, ได้ development `NekoProxyCore.exe` และ headless smokeผ่าน
3. **E3 — Launcher boundary:** Tasks 6-8, cross-repo testsผ่าน
4. **E4 — Real gate:** Task 9, sanitized E2E reportผ่าน

ให้ commit/stage แยกตาม repository และ checkpoint หลังตรวจ diff เท่านั้น; ห้าม commit/push โดยอัตโนมัติ และห้ามนำไฟล์ runtime/settingsจริงเข้า commit
