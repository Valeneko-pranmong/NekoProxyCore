# NekoProxyCore — handoff เริ่มจากจุดนี้

> **ARCHIVED HISTORICAL SNAPSHOT:** เก็บเพื่อ trace history เท่านั้น ดูสถานะปัจจุบันที่
> [`../../current/core-release-handoff.md`](../../current/core-release-handoff.md)

อัปเดต: 2026-08-03 · branch งาน: `feature/neko-headless` · baseline ที่ห้ามแก้:
`baseline/netch-1.9.7` (`99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687`)

## สถานะจริงของรอบนี้

Phase 2B, lifecycle seam ต้นทางของ 2C, concrete process resolver (Step C) และ Step D
ผ่านแล้ว. วันที่ 2026-08-03 official ProcessMode runner ผ่าน start → steady `Running` →
local SOCKS → traffic window 600 วินาที → stop/cleanup กับ `pso2.exe` จริง และคืน
`RUNNER exit=0`. Human gameplay verification เข้า `Central City` online lobby และ
server-side Shadowsocks TCP/UDP counters เพิ่มขึ้นระหว่าง gameplay ตาม
[processmode-test-report.md](processmode-test-report.md).

**Step D full integration gate เป็น historical pre-authorization PASS และใช้อนุมัติเริ่ม Step E development เท่านั้น**. ข้อความ static
`TRAFFIC_GATE result=RequiresTargetVerification` ของ runner ถูกปิดด้วย external evidence
ในรายงาน ไม่ใช่การ hard-code PASS. ยังไม่มี `NekoProxyCore.exe` หรือ production release;
downstream build/IPC/package/signing/clean-machine gates ยังเปิดอยู่

- เพิ่ม assembly ที่ไม่มี WinForms: `NekoProxyCore.Core/`
- มี `ProxyConfiguration`, `ProxyStartRequest`, `ProxyStatusKind`, `ProxyError`,
  `IProxyRuntime`, `IProxyStatusSink`, `IProcessResolver` และ
  `ProcessModeController`
- `HeadlessRuntimeCoordinator` มี start/stop/status, idempotent start/stop,
  cancellation, timeout และ typed/sanitized error
- Unit tests ใช้ fake process resolver/engine และ fixture identifier ที่ไม่มี credential;
  suite รอบ `Core-S0-Producer-01` มี 64 tests และผ่าน `64/64`. C1 เคยพบหนึ่งรอบ `50/51` จาก process-exit status race; C5 แก้ synchronization แล้วและ focused rerun ผ่าน `20/20`
- เพิ่ม concrete `NekoProxyCore.Windows/WindowsProcessResolver.cs` ที่ใช้
  `Process.Exited`/process handle, รองรับ cancellation และ fallback แบบ Windows event-based
  เมื่อ protected process ปฏิเสธ process handle
- เพิ่ม `NekoProxyCore.Legacy/` และ `NetchProcessModeEngine` พร้อม runtime-only
  `profile-N`/`server-N` mapping, typed status sink และ fake lifecycle tests
- `MainController`/`NFController` เริ่มใช้ injected `IProxyStatusSink`; UI callback
  อยู่ใน `MainFormProxyStatusSink` แทนการเรียกจาก controller
- Official sanitized integration runner/launcher ผ่าน lifecycle/local-SOCKS และ target
  traffic verification กับ target จริงแล้ว; IPC/headless production host เป็นงาน Step E

ทีม Tester ให้ใช้ [tester-handoff.md](tester-handoff.md) เป็น supporting procedure สำหรับ
คำสั่ง build/test, official runner, exit codes และเกณฑ์ PASS/FAIL/BLOCKED ส่วนทีมพัฒนา
ให้ใช้ [refactor-handoff.md](refactor-handoff.md) เป็น historical checkpoint ของ Phase 2

รายละเอียด contract, tests, build evidence และงานถัดไปอยู่ใน
[refactor-handoff.md](refactor-handoff.md). สถานะรวมและลำดับงาน canonical อยู่ที่
`D:\Audit Neko project\Proxy core to do\README.md`

## เริ่มงานอย่างปลอดภัย

```powershell
Set-Location D:\NekoProxyCore
git status -sb
git branch --show-current
git log -1 --oneline

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\neko-proxycore-preflight.ps1

dotnet restore .\Tests\Tests.csproj
dotnet build .\NekoProxyCore.Core\NekoProxyCore.Core.csproj -c Release --no-restore
dotnet build .\NekoProxyCore.Windows\NekoProxyCore.Windows.csproj -c Release --no-restore
dotnet test .\Tests\Tests.csproj -c Release --no-restore
```

Build `NekoProxyCore.Legacy` ทั้งสอง target ต้องใช้ x64 Visual Studio Developer
environment (MSBuild `17.14.51` ที่ยืนยันแล้ว), โดยตั้ง
`MSBuildSDKsPath=C:\Program Files\dotnet\sdk\6.0.428\Sdks` และ
`MSBuildEnableWorkloadResolver=false` เฉพาะ command นั้น แล้วรัน:

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
  .\NekoProxyCore.Legacy\NekoProxyCore.Legacy.csproj /m `
  /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
```

Preflight ล่าสุด: `PASS=34, WARN=3, FAIL=0` (exit code `2` เพราะ warning)

Warnings ที่ต้องรักษาไว้ในรายงาน:

1. worktree ยังมีงาน handoff และ Phase 2 ที่ยังไม่ commit — ตรวจ diff และอย่า
   discard งานของผู้ใช้
2. ไม่มี `wpcap.dll`/`Packet.dll`; ยอมรับได้เฉพาะ ProcessMode, ไม่พอสำหรับ
   PcapMode
3. `build.ps1` ดาวน์โหลด GeoLite2 โดยไม่มี URL/checksum ที่ pin แล้ว; artifact
   ใด ๆ จึงไม่ใช่ reproducible/release-grade

### อย่าสับสนกับ warning จาก `dotnet build Netch.sln`

`WARN=3` ข้างต้นคือผลจาก **preflight** เท่านั้น ส่วนภาพ build ล่าสุดมี warning
อีกชุดหนึ่งที่เป็นคนละ gate:

- `NETSDK1138` — `net6.0-windows` ของ Netch อยู่นอก support policy แล้ว
- `NU1503` สองรายการ — NuGet ข้าม restore ให้ `Redirector.vcxproj` และ
  `RouteHelper.vcxproj` เพราะเป็น C++ project

ภาพเดียวกันมี `MSB4278` เป็น **error** 7 รายการ ไม่ใช่ WARN: `dotnet build` ไม่ได้
ตั้งค่า `VCTargetsPath` ให้ C++ project แม้ไฟล์
`C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Microsoft\VC\v170\Microsoft.Cpp.Default.props`
มีอยู่จริง (`Test-Path=True`)

ตัว preflight หา Go ที่ `C:\Program Files\Go\bin\go.exe` ได้แม้ไม่ได้อยู่ใน
`PATH` แล้ว แต่ Go ที่พบคือ `1.26.5`; source `Other/aiodns` เก่าต้องใช้ Go
ก่อน 1.20 เมื่อต้อง rebuild helper นั้น

## ข้อห้ามและขอบเขต

- `NekoLauncher` เป็น UI/system tray เดียว; core ห้ามสร้าง WinForms, console,
  tray, balloon หรือ notification
- ProcessMode ของ PSO2 คือ MVP; ห้ามขยายไป TunMode/PcapMode ก่อน contract นี้
  เชื่อมกับ engine จริงและผ่าน integration test
- ห้ามส่งหรือบันทึก password/token/private key ผ่าน argv, source, fixture,
  log, dump หรือ artifact
- `ProxyConfiguration` รับเฉพาะ opaque identifier ที่ปลอดภัย; อย่าเปลี่ยนให้รับ
  URI หรือ credential เพื่อเชื่อม legacy code แบบลัด
- core assembly ห้ามอ้าง `Global.MainForm`, `System.Windows.Forms`, message box,
  `Invoke` หรือ `BeginInvoke`; UI adapter อยู่ฝั่ง host/launcher เท่านั้น
- ห้ามแก้ `baseline/netch-1.9.7` หรือใช้ `main` เป็น compatibility base

## ข้อจำกัด build ที่ยังเปิดอยู่

คำสั่ง `dotnet build Netch.sln -c Release` ไม่ใช่ build gate ที่ถูกต้องสำหรับ
C++ projects ของ solution นี้ ภาพล่าสุดจบที่ `Build failed with 7 error(s) and
3 warning(s)` แต่ `NekoProxyCore.Core` ยัง build สำเร็จแยกต่างหาก

Visual Studio MSBuild ที่ตรวจพบ (17.14.51) build native
`Redirector.bin` และ `RouteHelper.bin` ได้เมื่อเรียกผ่าน environment ที่ถูกต้อง
แต่ solution ทั้งชุดยังล้มเพราะ:

- หา `Microsoft.NET.Sdk` ไม่พบใน Visual Studio MSBuild installation
- `RedirectorTester` ขาด .NET Framework 4.8 targeting pack แบบ exact

การแก้ `VCTargetsPath` ชั่วคราวทำให้ C++ props ถูกพบ แต่เมื่อเรียกผ่าน dotnet SDK
จะเจอ `MSB8003` (`VCToolsInstallDir` ไม่ถูกกำหนด) และ `MSB4018` จาก
`Microsoft.Build.Utilities.CanonicalTrackedOutputFiles`; จึงต้องใช้ Visual Studio
Developer environment/real MSBuild และตรวจ SDK/targeting pack ให้ครบ ไม่ใช่
แก้ source project เพื่อหลบ error

อย่ารายงาน solution/release build ว่าผ่านจนกว่าจะแก้สองข้อข้างต้นและบันทึก
คำสั่ง, tool versions, artifact path และ SHA-256 ใหม่

## Step E security authorization gate

ก่อนดำเนิน production headless host/Launcher integration ให้อ่าน
`tools/STEP_E_SECURITY_AUTHORIZATION_REPORT.md` เป็น source of truth ด้าน authorization.
ทุกการเริ่ม Core ต้องได้รับ short-lived Backend-signed permit แบบ online-only ซึ่งผูกกับ
Core-generated one-use challenge, active account/license/installation/session/heartbeat และ
canonical start configuration. `CurrentUserOnly` named pipe และการตรวจ `pso2.exe` เป็นคนละ
precondition และใช้แทน authorization ไม่ได้

Step D ProcessMode/gameplay PASS ยังคงมีผลในฐานะ historical pre-authorization evidence แต่ Step E production host จัดเป็น
**SECURITY BLOCKED** จนกว่า Backend/Core/Launcher/Proxy Server จะปิด permit, replay,
revocation และ static reusable proxy credential gates ตามรายงานดังกล่าว

## ไฟล์ handoff

```text
tools/
├─ HANDOFF.md                  # หน้านี้: entry point และ hard gates
├─ REFACTOR_HANDOFF.md         # Phase 2 checkpoint และ flow ที่ต้องทำต่อ
├─ NEKOPROXYCORE_BUILD_PLAN.md # แผนผลิตภัณฑ์/build ระยะยาว
├─ TESTER_HANDOFF.md           # ขั้นตอน build, contract test และ integration gate สำหรับทีม Tester
├─ PROCESSMODE_TEST_REPORT.md  # Step D real gameplay/traffic evidence
├─ STEP_E_SECURITY_AUTHORIZATION_REPORT.md # Backend/Security actions และ revised Step E
├─ neko-proxycore-preflight.ps1
└─ run-processmode-integration.ps1 # official sanitized integration launcher
```
