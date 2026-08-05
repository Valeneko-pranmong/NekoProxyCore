# Historical Documentation Archive

ไฟล์ในโฟลเดอร์นี้เป็นหลักฐานย้อนหลัง ไม่ใช่สถานะปัจจุบันของ NekoProxyCore

> เริ่มจาก [`../current/core-release-handoff.md`](../current/core-release-handoff.md)
> และ [`../README.md`](../README.md) เสมอ

## Archive index

### CI

- [`ci/ci-maintenance-proposal-2026-08-05.md`](ci/ci-maintenance-proposal-2026-08-05.md) —
  static workflow-maintenance proposal จาก snapshot เก่า ไม่มี active CI run รองรับ

### Plans

- [`plans/initial-build-plan-2026-07-30.md`](plans/initial-build-plan-2026-07-30.md) —
  แผนเริ่มต้นก่อน canonical headless publish สำเร็จ

### Step D — historical pre-authorization evidence

- [`step-d/project-handoff-2026-08-03.md`](step-d/project-handoff-2026-08-03.md) —
  project handoff snapshot ที่เคยเป็น entry point และถูกแทนที่แล้ว
- [`step-d/refactor-handoff.md`](step-d/refactor-handoff.md) — refactor checkpoint
- [`step-d/tester-handoff.md`](step-d/tester-handoff.md) — tester procedure/checkpoint
- [`step-d/processmode-test-report.md`](step-d/processmode-test-report.md) — historical
  ProcessMode/gameplay evidence ก่อน production authorization

### Step E

- [`step-e/headless-host-launcher-boundary-plan-2026-08-03.md`](step-e/headless-host-launcher-boundary-plan-2026-08-03.md) —
  implementation plan ที่ดำเนินการแล้ว

### Security S0

- [`security-s0/central-production-adapter-handoff.md`](security-s0/central-production-adapter-handoff.md)
- [`security-s0/core-contract-proposal.md`](security-s0/core-contract-proposal.md)
- [`security-s0/core-producer-01-handoff.md`](security-s0/core-producer-01-handoff.md)
- [`security-s0/core-security-implementation-handoff.md`](security-s0/core-security-implementation-handoff.md)
- [`security-s0/launcher-core-authorization-adapter-handoff.md`](security-s0/launcher-core-authorization-adapter-handoff.md)
- [`security-s0/security-contract-freeze-request.md`](security-s0/security-contract-freeze-request.md)
- [`security-s0/step-e-security-authorization-report.md`](security-s0/step-e-security-authorization-report.md)
- [`security-s0/permit-verifier-test-matrix-draft.md`](security-s0/permit-verifier-test-matrix-draft.md) —
  draft matrix ที่ระบุ verifier เป็น skeleton; ถูกแทนที่ด้วย implementation/tests ปัจจุบัน

## Archive policy

- ห้ามแก้ตัวเลข/สถานะย้อนหลังให้ดูเป็น current; เพิ่ม archive note หรือ index แทน
- ซ่อม relative links ได้เมื่อย้ายไฟล์ เพื่อให้ historical context อ่านต่อได้
- ห้ามใช้ historical binary hash เป็นหลักฐานของ source revision ใหม่
- หากต้องคืนไฟล์เก่า ให้ดึงจาก Git history ไม่ใช่คัดลอกจาก generated artifact directory
