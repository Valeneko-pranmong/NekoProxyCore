# NekoProxyCore — Phase 2 refactor handoff

อัปเดต: 2026-08-02

เอกสารนี้บันทึก checkpoint ที่ตรวจแล้วสำหรับทีมถัดไปที่แยก runtime/network
engine ออกจาก Netch WinForms เดิม ให้เริ่มจาก [HANDOFF.md](HANDOFF.md) และรักษา
product contract ด้านล่างก่อนเปลี่ยน network behavior

## 1. Snapshot ที่ยืนยันแล้ว

| รายการ | ค่า |
|---|---|
| Workspace | `D:\NekoProxyCore` |
| Branch งาน | `feature/neko-headless` |
| HEAD ปัจจุบัน | `fda4dec9715a4e9693fc53e558944c44cf5caf9f` (`fda4dec Implement Step D ProcessMode adapter`) |
| Baseline ที่ pin | `baseline/netch-1.9.7` / `99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687` |
| Remote | `origin=Valeneko-pranmong/NekoProxyCore`, `upstream=netchx/netch` |
| Preflight ล่าสุด | `PASS=34, WARN=3, FAIL=0` (exit `2` จาก warning เท่านั้น) |
| Core build | `NekoProxyCore.Core` Release ผ่าน, 0 warnings |
| Contract/lifecycle + integration packaging tests | `dotnet test Tests/Tests.csproj -c Release --no-restore`: 23 passed |
| Windows adapter build | `NekoProxyCore.Windows` Release ผ่าน, 0 warnings |

> สถานะ worktree วันที่ 2026-08-02: Step D ผ่าน build และ contract-test verification
> ตามหลักฐานด้านล่างแล้ว แต่ยัง **ไม่ใช่** PSO2/redirector integration test จริง.
> ห้ามเริ่ม Step E หรืออ้างว่า redirector ส่ง traffic ได้จนกว่าจะปิด integration gate.

Worktree ใน checkpoint นี้ยังไม่สะอาดโดยตั้งใจ: มี `NekoProxyCore.Core/`,
`NekoProxyCore.Windows/`, `Tests/HeadlessRuntimeTests.cs`, `Netch.sln`,
`Tests/Tests.csproj`, `tools/neko-proxycore-preflight.ps1` และเอกสาร handoff
ที่ยังไม่ commit
ทีมถัดไปต้อง inspect/stage เฉพาะไฟล์ task นี้ และห้าม discard การเปลี่ยนแปลง
ที่มีอยู่

### Tooling ที่ใช้ยืนยัน

| Tool | ผลที่พบ |
|---|---|
| .NET SDK ที่ใช้ยืนยัน | `6.0.428` ที่ `C:\Program Files\dotnet\dotnet.exe` (ต้องเติม PATH ใน shell นี้) |
| Core target | `net6.0` (ไม่มี `-windows`, ไม่มี WinForms reference) |
| Visual Studio MSBuild | `17.14.51` |
| Go ที่ preflight พบ | `C:\Program Files\Go\bin\go.exe`, `go1.26.5` |

Go ที่ติดตั้งใหม่เกินรุ่นที่ source `Other/aiodns` เก่ารองรับ (ต้องก่อน 1.20)
ดังนั้น preflight ผ่านเพียงว่า toolchain พบ; ยังไม่ใช่หลักฐานว่า rebuild
`aiodns` ผ่าน

## 2. สิ่งที่เพิ่มใน checkpoint นี้

### 2.1 Core assembly แยกจาก UI

`NekoProxyCore.Core/NekoProxyCore.Core.csproj` เป็น `net6.0` class library ที่ไม่
reference `Netch`, WinForms หรือ native driver โดยตรง โค้ดสำคัญคือ:

| ไฟล์ | หน้าที่ |
|---|---|
| `ProxyConfiguration.cs` | immutable sanitized input: ProcessMode, process/profile/server opaque references และ timeout |
| `ProxyStartRequest.cs` | configuration + safe correlation id + cancellation token |
| `ProxyError*.cs` | error code และ redaction ของ assignment, bearer token, command-line secret และ URI user-info |
| `RuntimeContracts.cs` | `IProxyRuntime`, `IProxyStatusSink`, `IProcessResolver`, `IProxyModeController`, `IProcessModeEngine` |
| `HeadlessRuntimeCoordinator.cs` | state machine: Stopped → Starting → Running → Stopping → Stopped/Failed |
| `ProcessModeController.cs` | seam ที่ตรวจ target process ก่อน/หลัง engine start แล้ว delegate ไป engine adapter |

`ProxyConfiguration` และ correlation id จำกัดเป็น opaque identifier
`[A-Za-z0-9._-]` เพื่อป้องกัน credential/URI หลุดเข้ามาใน status หรือ log
หาก input ไม่ผ่านให้ใช้ `TryCreate(...)` และส่ง `ProxyErrorCode.InvalidConfiguration`
กลับไป ไม่ส่ง exception detail ไปยัง launcher

### 2.2 Contract coverage ที่ผ่าน

`Tests/HeadlessRuntimeTests.cs` ทดสอบด้วย fake process resolver/engine เท่านั้น
ไม่มี profile, server หรือ credential จริง:

- start → running → stop และ typed status events
- repeated stop เป็น idempotent; repeated start คืน `AlreadyRunning`
- process ไม่พบ และ process exit ระหว่าง start คืน typed error
- invalid/secret-like reference คืน `InvalidConfiguration`
- start/stop timeout และ cancellation คืน typed result
- error redaction ไม่คืน sentinel strings
- core assembly ไม่มี `System.Windows.Forms`/`WindowsBase` reference

ใช้ fixture identifier `pso2.exe`, `fixture-pso2`, `fixture-server` เท่านั้น
จึงยัง **ไม่ใช่** การทดสอบ PSO2 จริง, redirector driver จริง หรือ traffic จริง

ตรวจ source ซ้ำได้ด้วย:

```powershell
rg -n 'System\.Windows\.Forms|MainForm|MessageBoxX|NotifyIcon|Application\.Run|BeginInvoke|Invoke\(' `
  .\NekoProxyCore.Core --glob '*.cs' --glob '*.csproj'
```

ผลที่คาด: ไม่มี match

### 2.3 Concrete process resolver ที่เพิ่มในรอบนี้

`NekoProxyCore.Windows/WindowsProcessResolver.cs` เป็น implementation ฝั่ง host
ของ `IProcessResolver` ที่ใช้ `Process.GetProcessesByName` และ `Process.Exited`
โดยไม่สร้าง polling watcher ถาวร ตัว resolver:

- รับเฉพาะ executable name และ normalize `.exe` suffix; path/wildcard ถูกปฏิเสธ
- ตรวจ `HasExited` ก่อนและหลังผูก event เพื่อปิด race ตอน process จบระหว่าง setup
- รอ process ที่อยู่ใน snapshot ด้วย OS process handle/event และรองรับ cancellation
- แปลงความผิดพลาดจาก process inspection เป็น typed `StartFailed` ที่ไม่มีรายละเอียด
  path หรือ command line หลุดออกไป

เพิ่ม test ตรวจการค้นหา process ปัจจุบันทั้งแบบมี/ไม่มี `.exe` และ cancellation
ก่อนรอ ผลรวม contract tests ล่าสุดคือ `16 passed, 0 failed` เมื่อรวม resolver tests

## 3. Product invariants ห้ามละเมิด

- NekoLauncher เป็น UI และ system tray เพียงตัวเดียว
- NekoProxyCore ต้องไม่มี WinForms, console window, tray, balloon หรือ
  notification; ห้ามใช้ window hider, hidden desktop หรือ polling watcher
  เป็น permanent solution
- MVP จำกัดที่ PSO2 ProcessMode; PcapMode/TunMode อยู่นอก scope
- ห้ามเก็บ/ส่ง/เขียน plaintext password, token หรือ private key ใน source,
  profile/fixture, argv, log, dump หรือ artifact
- Core public status/error ต้องเป็น typed/sanitized data ไม่ใช่ UI/localized text
- ห้ามแก้ `baseline/netch-1.9.7`; ห้ามใช้ `main` เป็น compatibility base

## 4. Flow ที่ทีมถัดไปต้องทำ

```mermaid
flowchart TD
    A["Preflight and inspect dirty worktree"] --> B["Build Core and run contract tests"]
    B --> C["Freeze and commit Phase 2B checkpoint"]
    C --> D["Create concrete Windows IProcessResolver (done)"]
    D --> E["Create legacy ProcessMode engine adapter outside Core (done)"]
    E --> F["Publish official sanitized integration runner (done)"]
    F --> G["Run sanitized PSO2 ProcessMode integration gate (next)"]
    G --> H["Remove UI callbacks from the runtime path"]
    H --> I["Build/package only after native build gates pass"]
```

### Step A — Gate ก่อนแก้

```powershell
Set-Location D:\NekoProxyCore
git status -sb
git merge-base --is-ancestor 99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687 HEAD
if ($LASTEXITCODE -ne 0) { throw 'HEAD is not based on Netch 1.9.7' }

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\neko-proxycore-preflight.ps1
```

`FAIL>0` คือ stop condition; exit code `2` ที่มี WARN เท่านั้นต้องอ่าน warning
แต่ไม่ใช่ required blocker

### Step B — ยืนยัน checkpoint นี้ก่อนต่อยอด

```powershell
dotnet build .\NekoProxyCore.Core\NekoProxyCore.Core.csproj -c Release --no-restore
dotnet build .\NekoProxyCore.Windows\NekoProxyCore.Windows.csproj -c Release --no-restore
dotnet test .\Tests\Tests.csproj -c Release --no-restore
```

ถ้าจะ commit ให้ stage เฉพาะไฟล์ Phase 2 และเอกสาร handoff ที่ตั้งใจแก้ ห้าม stage
`bin/` หรือ `obj/` (`NekoProxyCore.Core/.gitignore` ป้องกันไว้แล้ว)

### Step C — Concrete process resolver (เสร็จแล้ว)

`NekoProxyCore.Windows/WindowsProcessResolver.cs` implement `IProcessResolver`
นอก Core แล้ว โดยใช้ `Process.GetProcessesByName` และ
`Process.EnableRaisingEvents`/`Exited` พร้อม cancellation, race re-check และ
safe process-name validation ผลทดสอบ resolver อยู่ในชุด `16 passed` ด้านบน

`ProcessModeController` เป็นผู้แปลง process ที่หายระหว่าง startup เป็น
`ProxyErrorCode.ProcessExited`; resolver เองไม่ส่ง path, profile หรือ secret ออกมา

### Step D — Legacy ProcessMode engine adapter

สร้าง adapter ที่ implement `IProcessModeEngine` ใน assembly ที่อนุญาตให้ depend
กับ Netch legacy/native code แล้วค่อย inject เข้า `ProcessModeController`:

- adapter เป็นเจ้าของ mapping จาก safe opaque reference ไปยัง runtime-only
  configuration; อย่าเพิ่ม username/password เข้า `ProxyConfiguration`
- ย้าย status จาก `Global.MainForm.StatusText` เป็น `IProxyStatusSink` ทีละจุด
  เริ่มที่ `Controllers/MainController.cs` และ `Controllers/NFController.cs`
- ให้ stop ใช้ `CancellationToken` และ `ProxyConfiguration.StopTimeout` ที่
  coordinator บังคับใช้อยู่แล้ว
- อย่าแตะ PcapController/TUNController ใน checkpoint เดียวกับ ProcessMode
- adapter/host ต้องไม่สร้าง Form, MessageBox, NotifyIcon หรือ console

สถานะ worktree ปัจจุบันของ Step D:

1. เพิ่ม `NekoProxyCore.Legacy/` ที่ target `net6.0` สำหรับ fake adapter tests และ
   `net6.0-windows` สำหรับ `NetchProcessModeSessionResolver` ซึ่ง reference `Netch`;
   `NekoProxyCore.Core` ไม่ reference กลับหา legacy/Netch
2. `NetchProcessModeEngine` implement `IProcessModeEngine` ผ่าน
   `ILegacyProcessModeSessionResolver`/`ILegacyProcessModeSession`; object ที่อาจมี
   credential ไม่ออกจาก assembly นี้
3. resolver รับเฉพาะ opaque `profile-N` และ `server-N`, ตรวจว่า profile/server/mode
   ProcessMode ใน legacy runtime ตรงกัน ก่อนเรียก `MainController` และ `NFController`
4. `MainController`/`NFController` เปลี่ยน lifecycle status เป็น `IProxyStatusSink`;
   `MainFormProxyStatusSink` เป็น UI adapter ฝั่ง WinForms แยกต่างหาก
5. `HeadlessRuntimeCoordinator` monitor process exit ผ่าน `IProcessExitWatcher` และ
   เรียก stop/cleanup โดยใช้ timeout ของ configuration
6. เพิ่ม fake tests ครอบ typed lifecycle, generic error/redaction, cancelled startup,
   process-exit cleanup และ monitor failure โดยไม่ใช้ PSO2/profile/credential จริง

สิ่งที่ยังห้ามประกาศ: **ยังไม่ยืนยัน** ว่า `ProcessModeController` เริ่ม redirector
หรือส่ง traffic จริงได้ จนกว่าจะผ่าน build/test และ sanitized PSO2 integration test.

#### หลักฐาน verification ของ Step D (2026-08-02)

- preflight (หลังมี verification changes): `PASS=34, WARN=3, FAIL=0`, exit `2`
  จาก warning เท่านั้น ได้แก่ worktree ที่ตั้งใจแก้, ไม่มี Npcap สำหรับ PcapMode และ
  GeoLite2 download ที่ยังไม่ reproducible
- `dotnet` `6.0.428`: build `NekoProxyCore.Core` และ `NekoProxyCore.Windows`
  แบบ Release ผ่านโดยไม่มี warning; `dotnet test Tests/Tests.csproj -c Release
  --no-restore` ผ่าน `23/23` (warning `SYSLIB0021` มีอยู่เดิมใน `Tests/Global.cs`)
- Visual Studio Developer environment / MSBuild `17.14.51` build
  `NekoProxyCore.Legacy/NekoProxyCore.Legacy.csproj` ด้วย `Configuration=Release`,
  `Platform=x64` ผ่านทั้ง `net6.0` และ `net6.0-windows`; เพื่อให้ MSBuild พบ SDK ที่
 ติดตั้งแยกต่างหาก ต้องตั้ง `MSBuildSDKsPath=C:\Program Files\dotnet\sdk\6.0.428\Sdks`
  และ `MSBuildEnableWorkloadResolver=false` เฉพาะ command นี้
- SHA-256 ของ development DLL ที่ได้:
  - `NekoProxyCore.Core/bin/Release/net6.0/NekoProxyCore.Core.dll` —
    `EBEE3D4EDCDA87752A8132B8733CE47361C7008824FD58A97F1B2196C46040E7`
  - `NekoProxyCore.Windows/bin/Release/net6.0/NekoProxyCore.Windows.dll` —
    `F723A74C29D2C1F801169C3DC21E32BD03A83D67321CF645DAC4DF5EED077C90`
  - `NekoProxyCore.Legacy/bin/x64/Release/net6.0/NekoProxyCore.Legacy.dll` —
    `B10C07A6A43AD8F734DFC273EBE91BE86F34D14950BEB14AD33BB68471BBB116`
  - `NekoProxyCore.Legacy/bin/x64/Release/net6.0-windows/NekoProxyCore.Legacy.dll` —
    `57BA019C56D6A1DF4217511978106F18E6E5379B0AA14EDB69F8331C9CAD370D`

PSO2 integration ยังเป็น blocker: เครื่องนี้มี `netfilter2` driver ทำงานอยู่ แต่ไม่พบ
process `pso2` หรือ `pso2_bin`; จึงไม่มีทางเริ่ม→running→stop กับเกมจริงโดยไม่สร้าง
fixture หรือส่ง credential. ต้องรัน gate นี้เมื่อมี PSO2 ที่ผู้ใช้เปิดอยู่และใช้เฉพาะ
opaque `profile-N`/`server-N` ที่มีอยู่ใน runtime.

เพิ่ม official runner ที่ `NekoProxyCore.IntegrationRunner/` และ launcher ที่
`tools/run-processmode-integration.ps1` แล้ว โดย pin `win-x64`, stage RID-specific
Windows runtime DLL ทุกไฟล์ใต้ `runtimes/win/lib/net6.0`, ตรวจ SHA-256 ก่อนรัน, whitelist output
และลบ runtime mirror ใน `%TEMP%` ผ่าน `finally`. `-PrepareOnly` ผ่าน และ negative
missing-process check คืน exit `20` โดยไม่เหลือ temporary directory. รายละเอียดการรัน
จริงและเกณฑ์ผลลัพธ์อยู่ใน [TESTER_HANDOFF.md](TESTER_HANDOFF.md).

### Handoff ให้ทีมถัดไป — ปิด verification ของ Step D ก่อน Step E

1. ติดตั้งหรือเปิด shell ที่มี .NET 6 SDK แล้วรัน `dotnet restore Tests/Tests.csproj`
   ก่อน แล้ว build `NekoProxyCore.Core`, `NekoProxyCore.Legacy` ทั้งสอง target และ
   `Tests/Tests.csproj`
2. ยืนยันว่า legacy Windows target build ได้ใน Visual Studio developer environment
   โดยไม่แก้ PcapController/TUNController เพื่อหลบ build error
3. รัน `tools/run-processmode-integration.ps1 -PrepareOnly` แล้วใช้ official launcher
   รัน sanitized PSO2 ProcessMode start → running → stop โดยไม่มี credential ใน argv/log
4. บันทึก command, tool version, test result และ artifact hash ใหม่ก่อนเริ่ม Step E

Stop condition: หากต้องเพิ่ม `Global.MainForm`, WinForms, MessageBox, tray,
console หรือส่ง secret ผ่าน argv/log ให้หยุดและออกแบบ seam ใหม่ก่อน

### Step E — Headless host และ launcher boundary

เมื่อ adapter ผ่าน test แล้วจึงเพิ่ม host executable แยกจาก Netch UI:

- `OutputType=WinExe` โดยไม่เปิด WinForms หรือ console
- รับเฉพาะ opaque profile/server reference ผ่าน IPC หรือ in-process boundary;
  ห้ามส่ง credential ทาง command line
- ส่ง typed status/error/correlation id ที่ผ่าน validation กลับ NekoLauncher
- NekoLauncher เป็นเจ้าของ tray, window, notification, retry และ localization
- อย่าลบ `MainForm` ขนาดใหญ่ครั้งเดียว; ค่อย ๆ ตัด runtime path ออกและคง legacy
  UI adapter จน ProcessMode E2E ผ่าน

## 5. ผล build และ blockers ที่ต้องรายงานต่อ

### แยกความหมายของ `WARN=3`

`WARN=3` จาก preflight ไม่ใช่จำนวน warning ใน compiler output โดยมีความหมาย
คงที่ดังนี้:

1. worktree มีการเปลี่ยนแปลงที่ยังไม่ commit (เป็นสถานะที่ต้อง inspect ไม่ใช่
   required failure)
2. ไม่พบ `wpcap.dll`/`Packet.dll` (ยอมรับได้สำหรับ ProcessMode แต่ block PcapMode)
3. `build.ps1` ดาวน์โหลด GeoLite2 โดยยังไม่มี pinned URL/checksum

จากภาพ `dotnet build .\Netch.sln -c Release` มี warning คนละชุด:

- `NETSDK1138` หนึ่งรายการ: `net6.0-windows` อยู่นอก support policy แล้ว
- `NU1503` สองรายการ: NuGet ข้าม restore ของ `Redirector.vcxproj` และ
  `RouteHelper.vcxproj` เพราะเป็น C++ project

ดังนั้น `3 warning(s)` ในภาพไม่ใช่หลักฐานว่า preflight เปลี่ยนเป็น WARN=6
และไม่ควรกลบ warning ใดด้วยการ suppress ก่อนมีแผน upgrade target framework
หรือ build C++ ที่ถูกต้อง

### ผ่าน — managed/adapter/integration-runner preparation

```text
dotnet build NekoProxyCore.Core/NekoProxyCore.Core.csproj -c Release --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build NekoProxyCore.Windows/NekoProxyCore.Windows.csproj -c Release --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test Tests/Tests.csproj -c Release --no-restore
Passed: 23, Failed: 0, Skipped: 0

powershell.exe -File tools/run-processmode-integration.ps1 -PrepareOnly
PREPARE runtime=win-x64 windowsRuntimeAssets=verified count=3
PREPARE_ONLY result=ready
```

Test project ยังเตือน `SYSLIB0021` จาก `Tests/Global.cs` เดิม (`SHA1CryptoServiceProvider`)
ไม่ใช่ warning ที่เพิ่มจาก Phase 2 core. Official runner publish อาจเตือน `MSB3277`
เรื่อง `WindowsBase` จาก legacy dependency; RID-specific Windows DLL ทั้ง 3 ไฟล์ถูก
stage และตรวจ SHA-256 แล้ว จึงต้องบันทึก warning ตามจริง ไม่ suppress เพื่อทำให้ผลดูสะอาด

`NekoProxyCore.Legacy/NekoProxyCore.Legacy.csproj` build ผ่านทั้ง `net6.0` และ
`net6.0-windows` ด้วย Visual Studio MSBuild `17.14.51`, `Configuration=Release`,
`Platform=x64` ตาม command ในหัวข้อ Step D และ [TESTER_HANDOFF.md](TESTER_HANDOFF.md).

### ยังไม่ผ่าน — full legacy solution/release build

`dotnet build .\Netch.sln -c Release` ยังไม่ใช่ gate ที่ผ่าน: dotnet CLI ไม่ได้ตั้ง
Visual C++ environment (`VCTargetsPath`/`VCToolsInstallDir`) และ full solution ยังมี
ข้อจำกัด `.NET Framework 4.8` targeting pack. Visual Studio MSBuild สร้าง
`RouteHelper.bin`/`Redirector.bin` ได้ แต่ยังห้ามเรียก full solution output ว่า clean,
reproducible หรือ release-grade. ข้อจำกัดนี้ไม่เปลี่ยนผลว่า Legacy adapter project build
ผ่านแล้ว และไม่ควรถูกใช้ย้อนสถานะ Step D managed verification

### Artifact boundary

development artifacts ที่ตรวจแล้วมี Core, Windows, Legacy ทั้งสอง target และ official
integration runner `win-x64`; SHA-256 ของ DLL หลักอยู่ในหัวข้อหลักฐาน verification ของ
Step D ด้านบน. Artifact เหล่านี้ถูก ignore และไม่ถูก commit

ยังไม่มี production `NekoProxyCore.exe`, package, signing, SHA-256 release manifest,
clean-machine evidence หรือ PSO2 target-traffic PASS ดังนั้นยังห้ามเริ่ม Step E/release

## 6. Acceptance checklist สำหรับ handoff รอบหน้า

- [x] Feature branch ยังคงสืบสายจาก pinned baseline
- [x] Preflight ไม่มี required FAIL
- [x] Headless contract start/stop/status tests ผ่านโดยไม่มี UI
- [x] Core assembly ไม่มี WinForms/UI reference
- [x] invalid config, repeated start/stop, process exit, timeout, cancellation และ
  redaction มี typed contract test
- [x] concrete process resolver แบบ event/handle-based
- [x] legacy ProcessMode adapter build ได้ทั้งสอง target และ fake lifecycle contract
  tests ผ่านโดยไม่พา UI เข้าสู่ core
- [ ] sanitized PSO2 ProcessMode start → running → stop integration test เพื่อยืนยันว่า
  เรียก redirector จริง
- [ ] headless host executable และ launcher adapter/IPC
- [ ] ตัด `Global.MainForm` และ UI callback ออกจาก runtime path จริง
- [ ] full native/managed build ผ่าน พร้อม tool versions/artifact SHA-256
- [ ] PcapMode/TunMode (นอก MVP) และ production package/release gates

## 7. Reference inventory ของ legacy UI coupling

จุดที่ยังต้องแยกใน Netch legacy code:

| จุด | งานถัดไป |
|---|---|
| `Netch/Global.cs` | แทน lazy `MainForm` dependency ด้วย injected host/runtime dependencies |
| `Netch/Program.cs` | แยก UI entry point จาก headless host entry point; เอา `Application.Run`, console และ MessageBox ออกจาก core path |
| `Controllers/MainController.cs` | จุดเริ่มต้นย้ายเป็น injected typed status sink แล้ว; runtime path อื่นยังต้องแยกต่อ |
| `Controllers/NFController.cs` | ส่ง driver/lifecycle status ผ่าน sink แล้ว; อย่า log secret |
| `Controllers/TUNController.cs` | ย้าย cancellation/timeout/result เมื่อ ProcessMode track ผ่านแล้ว |
| `Controllers/PcapController.cs` | แยก `LogForm`/`BeginInvoke`; นอก MVP |
| `Services/ModeService.cs` | คืน mode descriptors แทนแก้ ComboBox |
| `Utils/Bandwidth.cs` | ใช้ stoppable telemetry/status publisher |
| `Utils/SubscriptionUtil.cs` | เปลี่ยน NotifyTip เป็น structured event |
| `Utils/Utils.cs` | แยก window activation ออกจาก core path |

ก่อนย้ายแต่ละจุด ให้เพิ่ม/รักษา test แล้วรัน preflight, core build และ tests
