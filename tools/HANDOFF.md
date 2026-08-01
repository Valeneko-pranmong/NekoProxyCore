# NekoProxyCore handoff

สถานะ: เตรียม source และเครื่องมือตรวจ environment แล้ว แต่ baseline build
ยังถูกบล็อกด้วย build dependencies ที่ยังไม่ได้ติดตั้ง

## เริ่มต้นตรงนี้

อ่านและทำตามลำดับ:

1. อ่านเอกสารนี้จนจบ
2. อ่าน [NEKOPROXYCORE_BUILD_PLAN.md](NEKOPROXYCORE_BUILD_PLAN.md)
3. รัน [neko-proxycore-preflight.ps1](neko-proxycore-preflight.ps1)
4. ห้ามแก้ source หรือรายงานว่า build พร้อม หาก preflight ยังมี `FAIL`

```powershell
Set-Location F:\Github\NekoProxyCore
git status -sb
git branch --show-current
git log -1 --oneline

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\neko-proxycore-preflight.ps1
```

สำหรับ AI/CI:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\neko-proxycore-preflight.ps1 `
  -AsJson
```

Exit code:

- `0`: พร้อมเริ่มงาน
- `2`: มี warning ที่ต้องอ่าน แต่ไม่มี required blocker
- `1`: มี required blocker ให้หยุดก่อน

## Git contract

- Repository: `Valeneko-pranmong/NekoProxyCore`
- Branch พัฒนา: `feature/neko-headless`
- Branch อ้างอิง ห้ามแก้: `baseline/netch-1.9.7`
- Netch 1.9.7 pinned commit:
  `99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687`
- `main` ใช้ติดตาม upstream เท่านั้น ห้ามใช้เป็นฐาน MVP โดยไม่ทำ compatibility
  review
- ก่อนแก้ไฟล์ทุกครั้งต้องอ่าน `git status` และรักษางานที่มีอยู่ของผู้ใช้

ตรวจว่า HEAD ยังสืบสายจาก baseline:

```powershell
git merge-base --is-ancestor `
  99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687 HEAD
if ($LASTEXITCODE -ne 0) { throw 'HEAD is not based on Netch 1.9.7' }
```

## Product contract

- NekoLauncher เป็น UI และ System Tray เพียงตัวเดียว
- หลังพบเกม NekoProxyCore ต้องเริ่มแบบไม่มี WinForms, console, tray, balloon
  หรือ notification เพิ่มเติม
- ห้ามใช้ window hider, polling watcher หรือ hidden desktop เป็นคำตอบถาวร
- ห้ามเปลี่ยนสถานะหน้าต่างหรือ tray ของ NekoLauncher
- รักษา PSO2 `ProcessMode` เป็น MVP ก่อนพิจารณา TunMode
- ป้องกัน password ระดับพื้นฐาน: ไม่เก็บ plaintext, ไม่ส่งผ่าน command line
  และไม่เขียนลง log
- ห้ามฝัง credential จริง, log, dump, private key หรือ settings ของผู้ใช้ใน
  source/build artifact

## สถานะที่ยืนยันแล้ว

- Fork และ branch baseline/feature มีอยู่แล้ว
- Feature branch สืบสายจาก Netch 1.9.7
- Source files, native projects, driver และ runtime inputs หลักมีอยู่
- Preflight รองรับ human-readable output และ JSON
- PowerShell parser และ JSON parsing ผ่าน
- ยังไม่ได้ refactor network engine เป็น headless
- ยังไม่ได้ build baseline จาก source บนเครื่องนี้

## Required blockers ปัจจุบัน

ต้องติดตั้งและตรวจใหม่:

1. .NET SDK ที่ build `net6.0-windows` ได้
2. Visual Studio Build Tools 2022 พร้อม MSBuild
3. Desktop development with C++ / MSVC x64-x86
4. Windows 10 หรือ Windows 11 SDK
5. Go toolchain ที่เข้ากันกับ `Other/aiodns/go.mod` (`go 1.17`)

Npcap เป็น optional สำหรับ MVP ProcessMode แต่ต้องมีเมื่อ build/test PcapMode

การติดตั้ง dependency เป็นการเปลี่ยนระดับเครื่อง ต้องได้รับอนุญาตจากผู้ใช้ก่อน
AI ห้ามติดตั้งอัตโนมัติจากเอกสารนี้

## งานถัดไป

หลังผู้ใช้อนุมัติและติดตั้ง dependencies:

1. รัน preflight ซ้ำจน required checks ไม่มี `FAIL`
2. switch ไป `baseline/netch-1.9.7` และ build ต้นฉบับโดยไม่แก้ source
3. บันทึก tool versions, build command, artifact path และผลล้มเหลว/สำเร็จ
4. smoke test Netch baseline กับ PSO2 profile ที่ไม่มี credential จริง
5. กลับ `feature/neko-headless` ก่อนเริ่ม refactor
6. เริ่ม Phase 2 จากการแยก status/configuration ออกจาก `Global.MainForm`

อย่าข้าม baseline build เพราะหาก refactor แล้ว network มีปัญหา จะไม่สามารถแยกได้
ว่าเกิดจาก environment เดิมหรือการเปลี่ยน headless

## ไฟล์ส่งไม้ต่อ

```text
tools/
├─ HANDOFF.md
├─ NEKOPROXYCORE_BUILD_PLAN.md
└─ neko-proxycore-preflight.ps1
```

ทั้งสามไฟล์ต้องถูก commit บน `feature/neko-headless` และ worktree ต้องสะอาดก่อน
ส่งต่อให้ AI/นักพัฒนาคนถัดไป
