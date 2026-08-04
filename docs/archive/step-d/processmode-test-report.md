# ProcessMode Integration Test Report — Step D Final Handoff

> **ขอบเขตหลักฐาน:** รายงานนี้เป็น historical pre-authorization evidence ของ accepted Step D run เท่านั้น ไม่ใช่หลักฐานว่า current default-deny runner หรือ production Launcher → Core authorization ผ่าน
>
> ที่ revision `8d19f36` current `NekoProxyCore.IntegrationRunner` สร้าง `HeadlessRuntimeCoordinator` ผ่าน default constructor ซึ่งใช้ `AuthorizationRequiredStartAuthorizer`; start จึง fail closed และ runner ปัจจุบันไม่ reproduce trace/PASS ของ accepted Step D run นี้

วันที่ทดสอบ: 2026-08-03
Workspace: `D:\NekoProxyCore`
Branch: `feature/neko-headless`
HEAD ณ เวลาทดสอบ: `869dc80` (`Create PROCESSMODE_TEST_REPORT.md`)
สถานะ source: worktree มีการแก้ไข Step D ที่ยังไม่ commit; ให้ตรวจ `git status` และ diff ก่อนส่งต่อ

## 1. Executive summary

**ผลสุดท้าย: PASS — Step D full ProcessMode integration gate**

Step D ผ่านการทดสอบกับ `pso2.exe` จริงครบทั้ง managed lifecycle, local SOCKS,
character/login flow, online lobby, gameplay load และ server-side Shadowsocks traffic
correlation โดย runtime คงสถานะ `Running` ตลอด traffic window 600 วินาที ก่อน stop และ
cleanup สำเร็จ

**อนุมัติให้เริ่ม Step E ได้ในขอบเขต development handoff** ตาม gate ของ
`tools/TESTER_HANDOFF.md`. ผลนี้ไม่ใช่การอนุมัติ production release: full solution build,
headless production host, IPC, packaging, signing, clean-machine verification และ release
artifacts ยังเป็น gate แยกของ Step E/ขั้น release

## 2. Scope และ acceptance case

ทดสอบเฉพาะ ProcessMode MVP ด้วย opaque configuration references:

- Target process: `pso2.exe`
- Profile reference: `profile-0`
- Server reference: `server-0`
- Client OS: Windows 10
- Proxy implementation: `shadowsocks-libev` บน Ubuntu 22.04
- Traffic window: 600 วินาที

Approved target-traffic acceptance case:

1. ห้ามเริ่ม ProcessMode จนกว่าจะพบ `pso2.exe`
2. Start และตรวจ steady `Running`
3. Local SOCKS probe ต้องผ่าน
4. เปิด traffic window 600 วินาที
5. โหลดสถานะ Ship, เลือก Ship/ตัวละคร และเข้า `Central City` online lobby
6. สร้าง gameplay load โดยฟาร์มระหว่างที่ runtime ทำงาน
7. ตรวจ server-side TCP/UDP counters บน active Shadowsocks listener ซึ่งจำกัด source
   เป็นเครื่องทดสอบ
8. ยืนยันว่า counters เพิ่มขึ้นระหว่าง gameplay ไม่ใช่เพียงตอน startup
9. ยืนยัน runtime ยัง `Running` หลัง traffic window
10. Stop, repeated stop, controller cleanup และ orphan/temp cleanup ต้องผ่าน

ไม่มีการใช้ packet capture และไม่มีการเก็บ payload, credential, raw Shadowsocks config,
client/server address, listener port, password, cipher key, token หรือ private-key content
ในรายงานนี้

## 3. Official runner command

```powershell
& .\tools\run-processmode-integration.ps1 `
  -ProcessName pso2.exe `
  -ProfileReference profile-0 `
  -ServerReference server-0 `
  -TrafficWindowSeconds 600
```

Launcher ตรวจ target/driver ก่อนสร้าง runtime mirror และเริ่ม ProcessMode เฉพาะหลังพบ
`pso2.exe`. Runner publish เป็น `win-x64` และตรวจ RID-specific Windows runtime assets
ด้วย SHA-256 ก่อนรัน

## 4. Preconditions และ environment

| Check | Result |
|---|---|
| `pso2.exe` ก่อน Start | Running, count 1 |
| `netfilter2` | Running |
| Approved opaque profile/server mapping | Resolved |
| Windows runtime assets | Verified, count 3 |
| .NET SDK | 6.0.428 |
| Shadowsocks service | `shadowsocks-libev.service` active |
| Active Shadowsocks config candidates | 1 |
| Shadowsocks listeners | TCP and UDP |
| Container runtime | Not used |
| Temporary integration directories before run | 0 |

Runner publish มี warning `MSB3277` เรื่อง `WindowsBase` version conflict จาก legacy
Netch dependencies ตาม baseline แต่ publish สำเร็จ, runtime assets ผ่าน hash verification
และไม่มี build error จึงบันทึก warning ตามจริงโดยไม่ suppress

## 5. Client/runtime evidence

Observed sanitized output:

```text
PREPARE runtime=win-x64 windowsRuntimeAssets=verified count=3
PRECONDITION process=running count=1
PRECONDITION netfilter2=running
CONFIG profiles=1 servers=1 modes=1
EVENT status=Starting error=None
EVENT status=Running error=None
START success=True status=Running error=None
STEADY status=Running
SOCKS_PROBE success=True
TRAFFIC_WINDOW status=Ready durationSeconds=600
TRAFFIC_WINDOW status=Complete runtime=Running
EVENT status=Stopping error=None
EVENT status=Stopped error=None
STOP success=True status=Stopped error=None
STOP_AGAIN success=True status=Stopped error=None
CLEANUP controllers=clear
TRAFFIC_GATE result=RequiresTargetVerification
RUNNER exit=0
INTEGRATION_EXIT=0
```

Legacy layers publish duplicate typed eventsได้; การตัดสินใช้ลำดับ lifecycle และ terminal
state จริง ไม่มี `Failed` event ใน successful run

`TRAFFIC_GATE result=RequiresTargetVerification` เป็นข้อความ static ของ runner ซึ่งหมายถึง
exit `0` ไม่ได้ปิด traffic gate ด้วยตัวเอง ในรอบนี้ external target verification ได้ทำต่อและ
ผ่านตามหัวข้อ 6–7 ดังนั้น final classification เป็น PASS โดยไม่แก้หรือ hard-code output ของ
runner ให้ดูเหมือนผ่าน

## 6. Gameplay evidence

Human-in-the-loop evidence ยืนยันลำดับดังนี้:

1. Ship01–Ship10 โหลดสถานะ `Normal` ทั้งหมด ขณะที่ ProcessMode ทำงาน
2. เข้า Character Select บน `Ship04: Ansur`
3. เลือกตัวละครที่ได้รับอนุมัติสำเร็จ
4. เข้า `Central City` online shared lobby สำเร็จ
5. เห็น player HUD, online players และ live event/system information
6. ไม่พบ network error, disconnect หรือ timeout ใน accepted run
7. ฟาร์มต่อเนื่องเพื่อสร้าง gameplay traffic ระหว่าง traffic window

ระหว่าง gameplay ตรวจพบพร้อมกัน:

- `pso2.exe`
- `NekoProxyCore.IntegrationRunner.exe`
- `v2ray-sn.exe`
- `netfilter2` สถานะ `Running`

รอบก่อนหน้าที่ Ship statuses เป็น `Unknown` ไม่ใช่ product-failure evidence เพราะ runner
เวอร์ชันเดิม stop ProcessMode หลัง steady check 5 วินาทีก่อนผู้ใช้ทำ gameplay action. Runner
จึงถูกเพิ่ม bounded traffic window พร้อม regression testก่อนทำ accepted rerun

มีหนึ่งรอบที่ผู้ใช้ไปถึง Character Select แล้วเครื่อง client ดับก่อนจบรอบ; รอบนั้นจัดเป็น
**interrupted/unverified** และไม่นำมาใช้ตัดสิน PASS. Accepted run ภายหลังจบครบ 600 วินาที
โดย runtime ยัง `Running`

## 7. Server-side Shadowsocks metrics

### Measurement design

Server ใช้ `shadowsocks-libev` และไม่มี per-user metrics API มาตรฐาน จึงใช้ temporary
`iptables` counter-only rules สำหรับ TCP/UDP โดย:

- ผูกกับ active Shadowsocks listener ที่ resolve ภายใน server
- จำกัด source เป็นเครื่อง client ที่ทดสอบ
- เริ่มจาก baseline packets/bytes เท่ากับ 0
- ทำหน้าที่นับเท่านั้น ไม่ `ACCEPT`, `DROP`, NAT หรือเปลี่ยน routing
- ไม่อ่าน payload และไม่แสดง endpoint/config/credential ในหลักฐาน

### Samples

| Sample | TCP packets | TCP bytes | UDP packets | UDP bytes | Total packets | Total bytes |
|---|---:|---:|---:|---:|---:|---:|
| Baseline | 0 | 0 | 0 | 0 | 0 | 0 |
| Gameplay sample 1 | 3,977 | 369,537 | 11 | 1,100 | 3,988 | 370,637 |
| Gameplay sample 2 (+90s) | 8,577 | 1,056,066 | 16 | 1,600 | 8,593 | 1,057,666 |
| Final | 21,283 | 2,968,406 | 28 | 2,800 | 21,311 | 2,971,206 |

Delta ระหว่าง gameplay sample 1 และ sample 2:

```text
PACKETS_DELTA=4605
BYTES_DELTA=687029
TCP_PACKETS_DELTA=4600
TCP_BYTES_DELTA=686529
UDP_PACKETS_DELTA=5
UDP_BYTES_DELTA=500
```

Counters เพิ่มขึ้นต่อเนื่องขณะผู้ใช้ฟาร์ม และตรงกับ traffic window ที่ `pso2.exe`, runner,
local proxy helper และ driver ทำงานพร้อมกัน จึงเป็น direct server-side correlation ว่า traffic
จากเครื่องทดสอบถึง Shadowsocks listener จริง

ข้อจำกัด: counter จำกัดได้ถึง source client + Shadowsocks listener แต่ไม่สามารถ tag packet
ด้วย Windows PID ฝั่ง server ได้ การผูกกับ `pso2.exe` จึงใช้ controlled test window,
ProcessMode-only configuration, simultaneous process evidence และ gameplay action ร่วมกัน
ไม่ควรนำ total interface counters ที่ไม่ได้จำกัด source/listener มาใช้แทนหลักฐานนี้

## 8. Cleanup และ safety verification

หลัง accepted run:

```text
TEMP_AFTER=0
PSO2_AFTER=1
NETFILTER2_AFTER=Running
RUNNER_ORPHANS=0
V2RAY_ORPHANS=0
SERVER_COUNTER_RULES_REMAINING=0
SHADOWSOCKS_SERVICE=active
```

- Temporary `NekoProcessModeIntegration-*` ถูกลบ
- IntegrationRunner และ `v2ray-sn.exe` ไม่มี orphan
- `pso2.exe` ไม่ถูกหยุด
- `netfilter2` ไม่ถูกหยุด
- Temporary TCP/UDP firewall counters ถูกลบครบ
- Shadowsocks service ไม่ถูก restart/stop และยัง active
- SSH private key ไม่ถูกอ่านหรือบันทึกใน report; local `tools/*.pem` ถูกเพิ่มใน `.gitignore`
  และ key ไม่ถูก Git track

หมายเหตุด้าน operation: การหยุด local SSH watchdog ไม่ได้ trigger remote shell trap ตามที่
คาด จึงต้อง explicit-delete counter rules แล้วตรวจซ้ำจนเหลือ 0. รอบอนาคตควรใช้ remote
self-cleanup ที่ไม่ผูก lifecycle กับ local SSH process และต้องตรวจ remaining-rule count ทุกครั้ง

## 9. Automated test/build evidence

ก่อน accepted integration run:

- Traffic-window/packaging focused tests: `3/3` passed
- Focused ProcessMode/launcher testsจาก verification ก่อนหน้า: `8/8` passed
- Full `Tests/Tests.csproj` suite ณ accepted Step D run: `27/27` passed (historical count)
- C1 historical checkpoint มี 51 tests และ observed flaky run `50/51`; รอบ `Core-S0-Producer-01` มี 64 tests, full suiteผ่าน `64/64` และ C5 process-exit stability ผ่าน `20/20` โดยยังแยกจาก accepted Step D evidence นี้
- `NekoProxyCore.IntegrationRunner` Release `win-x64`: build succeeded, 0 errors
- Official `-PrepareOnly`: `PREPARE_ONLY result=ready`
- `git diff --check`: ผ่าน; มีเพียง LF/CRLF conversion warnings

Traffic-window change ใช้ test-first workflow: packaging test ล้มก่อนเพราะ runner/launcher
ยังไม่มี traffic window จากนั้น implementation ถูกเพิ่มและ focused suite กลับเป็น green

## 10. Defects/risks ที่ปิดใน Step D

1. Protected process monitor: `Process.HasExited`/event handle อาจถูกปฏิเสธ ทำให้ runtime
   เปลี่ยนจาก `Running` เป็น `Failed / StartFailed`; Windows event-based fallback แก้ path นี้
2. Launcher exit propagation: Windows PowerShell เคยอ่าน `ExitCode` เป็นค่าว่างหลัง timed
   wait; launcher รอ stream/process finalization, refresh และอ่าน typed integer แล้ว
3. Gameplay window: runner เดิม stop หลัง 5 วินาที ทำให้ human gameplay verification ทำไม่ทัน;
   เพิ่ม bounded `TrafficWindowSeconds` ช่วง 0–900 วินาทีและ runner timeout ที่สัมพันธ์กัน
4. SSH key hygiene: key ใต้ `tools/` เคยเป็น untracked และไม่ถูก ignore; เพิ่ม `/tools/*.pem`
   ใน `.gitignore` และยืนยัน `KEY_IGNORED=True`, `KEY_TRACKED=False`

## 11. Final classification และ handoff

| Gate | Result |
|---|---|
| Managed lifecycle | PASS |
| Protected target monitoring | PASS |
| Local SOCKS | PASS |
| Ship status/network readiness | PASS |
| Character selection | PASS |
| Online lobby | PASS |
| Gameplay load | PASS |
| Server-side Shadowsocks traffic correlation | PASS |
| 600-second runtime stability | PASS |
| Stop/repeated stop/controller cleanup | PASS |
| Temp/orphan/server-rule cleanup | PASS |
| Secret/payload sanitization | PASS |
| **Step D full ProcessMode integration gate** | **PASS** |
| **Step E development start authorization** | **GRANTED** |
| Production release authorization | NOT GRANTED — separate downstream gates remain |

ทีม Backend/PM สามารถเริ่ม Step E (headless host และ launcher boundary) ได้ โดยยังต้องรักษา
opaque references, typed/sanitized status, bounded waits และ cleanup semantics จาก Step D

ทีม Server ไม่ต้องคง firewall counters หรือเปลี่ยน Shadowsocks configuration จากการทดสอบนี้
และไม่ควรนำ endpoint/credential เข้า source, issue, report หรือ build artifact รอบถัดไป
