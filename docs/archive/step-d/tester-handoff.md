# NekoProxyCore — Tester handoff สำหรับ Step D

อัปเดต: 2026-08-03
Branch: `feature/neko-headless`
Baseline ที่ห้ามแก้: `baseline/netch-1.9.7` / `99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687`

## สถานะที่ส่งต่อ

ทีมพัฒนา verify แล้ว:

- Preflight: `PASS=34, WARN=3, FAIL=0` (exit code `2` เพราะ warning เท่านั้น)
- Core และ Windows adapter Release build ผ่าน 0 warnings
- Legacy adapter build ผ่านทั้ง `net6.0` และ `net6.0-windows` ด้วย Visual Studio
  MSBuild `17.14.51`
- `Tests/Tests.csproj` มี 64 tests ในรอบ `Core-S0-Producer-01` และ full managed rerun ผ่าน `64/64`; observed C1 flaky run `50/51` ถูกแก้ synchronization แล้วและ focused rerun ผ่าน `20/20`

วันที่ 2026-08-03 Step D full ProcessMode integration gate ผ่านกับ `pso2.exe` จริง:
lifecycle/local-SOCKS ผ่าน, runtime คง `Running` ครบ traffic window 600 วินาที, เข้า
Character Select และ `Central City` online lobby, สร้าง gameplay load และยืนยัน
server-side TCP/UDP Shadowsocks counter delta แล้ว ก่อน stop/stop ซ้ำและ cleanup สำเร็จ
พร้อม `RUNNER exit=0`. ดูหลักฐาน sanitized ที่
[PROCESSMODE_TEST_REPORT.md](PROCESSMODE_TEST_REPORT.md). **อนุมัติให้เริ่ม Step E ใน
ขอบเขต development; production release gates ยังเป็นงานแยก**

## ข้อกำหนดความปลอดภัย

- ใช้เฉพาะ opaque references รูปแบบ `profile-N`, `server-N` เช่น `profile-0` และ
  `server-0`
- ห้ามใส่ username, password, token, private key หรือ URI ที่มี user-info ใน argv,
  source, fixture, environment ที่บันทึกลงไฟล์, log, dump หรือ test artifact
- ห้ามคัดลอก/แนบ settings หรือ profile จริงลงในรายงาน
- `Original setting/` เป็น local runtime input และถูก ignore; ห้าม stage, commit,
  archive หรือส่ง directory นี้ให้ทีมอื่น
- Official launcher ใช้ได้เฉพาะ `Original setting/` ใต้ repository นี้ และไม่รับ
  runtime root จาก argv เพื่อไม่เปิด code-loading trust boundary ไปยัง path ภายนอก
- ProcessMode เท่านั้นในรอบนี้ ห้ามขยายไป PcapMode/TunMode
- ห้ามแก้ `baseline/netch-1.9.7`, `PcapController` หรือ `TUNController`
- Core ห้ามอ้าง WinForms, `Global.MainForm`, message box, tray หรือ console

## 1. Preflight

ใช้ PowerShell ใน root ของ repository:

```powershell
Set-Location D:\NekoProxyCore
$env:Path = 'C:\Program Files\dotnet;' + $env:Path

git status -sb
git branch --show-current
git log -1 --oneline

& .\tools\neko-proxycore-preflight.ps1
$preflightExit = $LASTEXITCODE
"PREFLIGHT_EXIT=$preflightExit"
```

ผลที่ยอมรับได้สำหรับ tester คือ `FAIL=0`; exit `2` ที่เกิดจาก warning เท่านั้นไม่ใช่
failure แต่ต้องบันทึก warning ทั้งหมดในรายงาน

## 2. Managed build และ contract tests

ต้องมี .NET SDK `6.0.428` หรือ SDK ที่รองรับ `net6.0`:

```powershell
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnet restore .\Tests\Tests.csproj
& $dotnet build .\NekoProxyCore.Core\NekoProxyCore.Core.csproj -c Release --no-restore
& $dotnet build .\NekoProxyCore.Windows\NekoProxyCore.Windows.csproj -c Release --no-restore
& $dotnet test .\Tests\Tests.csproj -c Release --no-restore
```

Acceptance สำหรับ checkpoint เดิม: Core/Windows build สำเร็จ. ตัวเลข 51 tests เป็น C1 historical checkpoint; suite รอบ `Core-S0-Producer-01` มี 64 tests และ C5 process-exit stability ผ่าน focused rerun `20/20`.
`SYSLIB0021` จาก `Tests\Global.cs` เป็น warning เดิมที่ไม่เกี่ยวกับ Step D

## 3. Legacy Windows-target build

อย่าใช้ `dotnet build` ตรง ๆ กับ `NekoProxyCore.Legacy` เพราะ SDK CLI ใน environment นี้
ไม่ resolve resource ของ Netch ได้ถูกต้อง ให้เปิด **x64 Native Tools Command Prompt
for VS 2022** แล้วรัน:

```bat
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\6.0.428\Sdks
set MSBuildEnableWorkloadResolver=false
msbuild.exe D:\NekoProxyCore\NekoProxyCore.Legacy\NekoProxyCore.Legacy.csproj /m /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
```

Acceptance: มีผลลัพธ์ทั้งสอง target:

- `NekoProxyCore.Legacy\bin\x64\Release\net6.0\NekoProxyCore.Legacy.dll`
- `NekoProxyCore.Legacy\bin\x64\Release\net6.0-windows\NekoProxyCore.Legacy.dll`

บันทึก MSBuild version, command และ error/warning ที่พบ ห้าม suppress warning หรือแก้
legacy source เพื่อหลบ build error

## 4. Static safety checks

รันจาก repository root และแนบเพียงผลว่าพบ/ไม่พบ ไม่ต้องแนบไฟล์ settings:

```powershell
rg -n 'System\.Windows\.Forms|Global\.MainForm|MessageBox|NotifyIcon|Application\.Run|BeginInvoke|Invoke\(' `
  .\NekoProxyCore.Core --glob '*.cs' --glob '*.csproj'

rg -n -i 'password|passwd|token|private[ _-]?key|authorization:|bearer ' `
  .\NekoProxyCore.Core .\NekoProxyCore.Legacy .\Tests --glob '*.cs' --glob '*.csproj'
```

Core command แรกต้องไม่มี match. Command ที่สองต้องตรวจด้วยตนเองว่าเป็นชื่อ field,
redaction test หรือข้อความปลอดภัยเท่านั้น และไม่มี credential จริง

## 5. Sanitized PSO2 ProcessMode integration gate

### Preconditions

1. ผู้ใช้เปิด PSO2 ด้วย installation ที่อนุมัติแล้ว และตรวจว่ามี process `pso2.exe` หรือ
   `pso2_bin.exe`
2. `netfilter2` driver/service ทำงานและ artifact native ที่จำเป็นอยู่ใน installation
3. มี profile/server ที่ผู้ใช้อนุมัติ โดย tester รู้เพียง opaque references เช่น
   `profile-0` และ `server-0`; ห้ามอ่านหรือรายงาน credential ภายใน
4. ใช้ official runner `NekoProxyCore.IntegrationRunner` ผ่าน
   `tools/run-processmode-integration.ps1`; ห้ามคัดลอกเฉพาะ top-level `*.dll`
   หรือสร้าง harness/report generator เอง เพราะจะเลือก platform-neutral
   `System.ServiceProcess.ServiceController.dll` แทน Windows runtime asset ได้

### เตรียม official runner

หลัง build Release artifacts ตามข้อ 2–3 แล้ว รัน smoke preparation ก่อนเปิด gate:

```powershell
& .\tools\run-processmode-integration.ps1 -PrepareOnly
```

ผลที่ต้องได้:

```text
PREPARE runtime=win-x64 windowsRuntimeAssets=verified count=3
PREPARE_ONLY result=ready
```

Script publish runner เป็น `win-x64`, ตรวจ SHA-256 ของ RID-specific DLL ทุกไฟล์ใต้
`runtimes\win\lib\net6.0\` กับไฟล์ที่ staged และ fail-fast ถ้า artifact ไม่ตรงกัน

`dotnet publish` อาจรายงาน warning `MSB3277` เรื่อง `WindowsBase` จาก legacy Netch
dependencies; บันทึก warning ตามจริง แต่ไม่ถือเป็น PASS/FAIL ของ runtime gate ตราบใดที่
publish สำเร็จและ `windowsRuntimeAssets=verified`. ห้าม suppress warning ใน project
เพื่อทำให้รายงานดูสะอาด

### Test flow

1. เรียก preflight และเก็บผลลัพธ์ก่อนเริ่ม
2. เปิด target process ที่ได้รับอนุมัติ แล้วรัน official launcher:

   ```powershell
   & .\tools\run-processmode-integration.ps1 `
     -ProcessName pso2.exe `
     -ProfileReference profile-0 `
     -ServerReference server-0
   ```

3. Launcher ต้องตรวจ process/driver, สร้าง runtime mirror ใน `%TEMP%`, เรียก
   coordinator ด้วย `ProxyModeKind.Process` และ opaque references เท่านั้น
4. ต้องได้ typed status `Starting → Running`, `STEADY status=Running`, local SOCKS
   probe ผ่าน และไม่มี secret ใน status/error
5. ตรวจ traffic path ของ target จริงตาม test case ที่อนุมัติแยกจาก local SOCKS probe
   (ห้ามใช้ packet capture ที่เก็บ payload หรือ credential)
   Accepted Step D case คือเข้า online lobby/สร้าง gameplay load ขณะ ProcessMode `Running`
   และตรวจ counter delta บน active Shadowsocks listener ที่จำกัด source เป็นเครื่องทดสอบ
6. Runner ต้องได้ `Stopping → Stopped`, stop ซ้ำสำเร็จ และ controller cleanup เป็น
   `clear`
7. Launcher ต้องคืน runner exit code จริง และลบ temporary runtime mirror ใน `finally`
   ทั้งกรณีสำเร็จ/ล้มเหลว
8. ทดสอบ cancellation/timeout และ process exit ตามที่ environment อนุญาต แล้วตรวจว่า
   ไม่มี orphan process/service state

### เกณฑ์ผลลัพธ์

- **PASS**: start/running/stop ครบ, typed statuses ถูกต้อง, cleanup สำเร็จ และไม่มี
  credential ใน output/artifact
- **FAIL**: lifecycle หรือ cleanup ผิด, status ไม่ typed/sanitized, หรือมี secret leak
- **BLOCKED**: ไม่มี PSO2 process, driver/native dependency ไม่พร้อม หรือไม่มี opaque
  profile/server ที่ผู้ใช้อนุมัติ — ห้ามรายงานเป็น PASS

สถานะ historical ของ accepted run คือ **Step D full ProcessMode integration gate PASS;
external target traffic verification ผ่านด้วย server-side Shadowsocks counters**. หลักฐานนี้
เกิดก่อน production authorization gate และห้ามใช้แทน authorized production E2E.

### Exit-code matrix ของ official launcher

| Exit | ความหมาย | Classification |
|---:|---|---|
| `0` | lifecycle/local SOCKS/stop/cleanup ผ่าน | ยังต้องตรวจ target traffic ก่อนสรุป PASS |
| `20` | ไม่พบ target process | BLOCKED |
| `21` | `netfilter2` ไม่ได้ Running | BLOCKED |
| `22` | runner เกิน bounded timeout (`TrafficWindowSeconds + 180` วินาที) และถูก terminate ทั้ง process tree | FAIL; ตรวจ orphan state ก่อนรันใหม่ |
| `2` | runtime start ไม่สำเร็จ | FAIL |
| `3` | steady state, SOCKS probe หรือ cleanup ไม่ครบ | FAIL |
| `4` | typed `ProxyRuntimeException` | FAIL; รายงานเฉพาะ error code |
| `5` | unhandled exception | FAIL; ห้ามแนบ raw exception/config log |

ถ้า output มี `TRAFFIC_GATE result=RequiresTargetVerification` ต้องทดสอบ traffic ของ
target จริงต่อ แม้ launcher คืน `0`; launcher exit `0` เพียงอย่างเดียวไม่ปิด gate

สำหรับ accepted run วันที่ 2026-08-03 ข้อความนี้ถูกปิดด้วย external verification ตาม
`PROCESSMODE_TEST_REPORT.md`: gameplay ผ่านและ Shadowsocks TCP/UDP counters เพิ่มขึ้นใน
controlled traffic window. ห้ามเปลี่ยนข้อความ runner เป็น PASS แบบ hard-code

## 6. สิ่งที่ต้องส่งกลับ

ส่งรายงานสั้น ๆ ที่มี:

- commit/branch และ OS
- preflight summary และ exit code
- .NET/Visual Studio MSBuild versions
- command ที่รัน, build/test counts และ artifact path + SHA-256
- integration result (`PASS`, `FAIL` หรือ `BLOCKED`) พร้อม error code แบบ sanitized
- ยืนยันว่าไม่มี password/token/private key อยู่ใน report, log หรือ artifact
- ยืนยันว่าไม่มี `NekoProcessModeIntegration-*` ค้างใน `%TEMP%` และไม่มี orphan
  `v2ray-sn.exe`/integration runner หลังจบ

ห้ามสร้าง script ที่เขียน PASS trace/report แบบ hard-code และห้ามใช้
`GenerateEvidence.ps1`, `IntegrationTestRun.log` หรือรายงานจากรอบก่อนเป็นหลักฐาน

ห้ามเริ่ม Step E จนกว่า integration gate จะเป็น `PASS`. Gate นี้ผ่านแล้วตามรายงานวันที่
2026-08-03 จึงเริ่ม Step E development ได้ แต่ยังห้ามสร้าง/อนุมัติ production release
package จนกว่า downstream build, host/IPC, packaging, signing และ clean-machine gates จะผ่าน
