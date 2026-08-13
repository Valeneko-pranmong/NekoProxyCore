# NekoProxyCore Documentation

เอกสารใน repository นี้แบ่งตามอายุและหน้าที่อย่างชัดเจน ห้ามใช้ไฟล์ใน `archive/`
ตัดสินสถานะปัจจุบันโดยไม่ตรวจ `current/` ก่อน

## ใช้งานปัจจุบัน

| เอกสาร | ใช้เมื่อ |
|---|---|
| [`current/core-release-handoff.md`](current/core-release-handoff.md) | ตรวจสถานะ Core, release bundle, security gates และสิ่งที่ส่งให้ Launcher |
| [`current/neko-auth-lite-core-contract.md`](current/neko-auth-lite-core-contract.md) | NEKO-AUTH-LITE/lite-v1 Core permit, challenge, replay และ launch-boundary contract |
| [`../README.md`](../README.md) | เริ่มต้นใช้งาน repository, build/test/publish และดูโครงสร้าง source |

`docs/current/` ต้องมีเฉพาะเอกสารที่ยังเป็น source of truth ปัจจุบัน เมื่อเอกสารถูกแทนที่
ให้ย้ายเข้า `docs/archive/<topic>/` พร้อมวันที่หรือ phase ในชื่อไฟล์

## Reference ที่ยังใช้

| เอกสาร | บทบาท |
|---|---|
| [`reference/legacy-netch-upstream.md`](reference/legacy-netch-upstream.md) | README ของ upstream Netch เดิมสำหรับ compatibility/reference |

Reference อธิบาย contract, upstream หรือวิธีคิดที่ยังใช้ได้ แต่ไม่ใช่ progress/status report

## Historical archive

ดู [`archive/README.md`](archive/README.md) สำหรับรายการทั้งหมด

| กลุ่ม | เนื้อหา |
|---|---|
| `archive/plans/` | แผน build/implementation ที่ดำเนินการแล้วหรือถูกแทนที่ |
| `archive/step-d/` | ProcessMode integration checkpoints ก่อน authorization |
| `archive/step-e/` | Headless host/Launcher boundary plan ที่ดำเนินการแล้ว |
| `archive/security-s0/` | Proposal, handoff และ freeze-request snapshots ของ S0 |
| `archive/ci/` | CI investigation/proposal ณ เวลาใดเวลาหนึ่ง ไม่ใช่ queue ปัจจุบัน |

เอกสาร archive อาจมี path, test count, commit, toolchain และสถานะที่ล้าสมัย เก็บไว้เพื่อ
trace history เท่านั้น

## Generated outputs

Generated build/test artifacts ไม่เก็บใน Git:

- `artifacts/` — legacy verification snapshots ที่ถูกแทนที่แล้ว
- `.hermes-verify-dotnet/` — temporary .NET runtime/SDK subset
- `TestResults/` — local build/test output
- `release/` — runtime bundles และ release metadata สำหรับส่งมอบแยกจาก Git

สร้าง output ใหม่จาก source และ canonical commands ใน root README/current handoff แทนการ
นำ historical binary snapshot กลับมาใช้

## Naming rules

- ใช้ lowercase kebab-case สำหรับชื่อเอกสาร
- Current status ใช้ชื่อที่บอก deliverable เช่น `core-release-handoff.md`
- Archive snapshot ใส่ phase/topic และวันที่เมื่อจำเป็น เช่น
  `project-handoff-2026-08-03.md`
- หลีกเลี่ยงชื่อกว้าง เช่น `HANDOFF.md`, `STATUS.md` หรือ `REPORT.md` ที่ไม่บอกขอบเขต
- ลิงก์ภายใน repository ใช้ relative Markdown links
