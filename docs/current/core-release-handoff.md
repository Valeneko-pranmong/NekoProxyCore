# NekoProxyCore — Core Release Handoff

- **ผู้รับ:** Launcher, Backend/Security, QA และ Release Teams
- **ผู้จัดทำ:** NekoProxyCore Team
- **วันที่:** 2026-08-05
- **Repository:** `E:\Github\NekoProxyCore`
- **Implementation branch:** `feature/neko-headless`
- **Verified runtime/package source revision:** `ce365ebccf976a752854609d2f42d738bfbfd039`
- **สถานะ:** `CANONICAL WIN-X64 PUBLISH LOCALLY VERIFIED — PRODUCTION AUTHORIZATION/SIGNING MATERIAL BLOCKED`

> เอกสารนี้เป็น source of truth สำหรับ Core handoff หลังปิด implementation รอบปัจจุบัน
> release artifacts ส่งมอบแยกจาก Git และยังไม่ใช่ production authorization/signing approval

## 0. Fresh authorization-composition update — 2026-08-05

ตรวจจาก `feature/neko-headless` ที่ revision
`ce365ebccf976a752854609d2f42d738bfbfd039` และ frozen baseline
`99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687`:

- เพิ่ม `NekoProxyCore.Core/ProductionAuthorizationComposition.cs` เพื่อ pin
  `NEKO-AUTH-S0/s0-rc1` และ package SHA-256
  `6697351b6b280afc566fedaaa1a6cfe207b1ea1d803c2eb613b4c1a891e192df`
- `NekoProxyCore.Host/Program.cs` เรียก composition root นี้อย่าง explicit
- composition ปัจจุบันคืน `AuthorizationRequiredStartAuthorizer` เท่านั้น เพราะไม่มี
  approved immutable public-key release/trusted-time composition ใน workspace
- เพิ่ม tests ยืนยัน contract identity และยืนยันว่าปัจจุบัน start ถูกปฏิเสธด้วย
  `AuthorizationRequired`

Fresh source-tree verification หลังแก้ canonical publish graph:

```text
dotnet test Tests/Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo
Passed: 103, Failed: 0, Skipped: 0

dotnet publish NekoProxyCore.Host/NekoProxyCore.Host.csproj \
  -c Release -f net6.0-windows -r win-x64 -p:Platform=x64 \
  --self-contained false -o TestResults/canonical-release-final --nologo -m:1
Publish: passed
```

Canonical publish นี้สร้างโดยตรงจาก working tree ปัจจุบันโดยไม่ใช้ Audit overlays:

```text
File count: 243
PDB/runtime-generated files: 0
NekoProxyCore.exe: 151,040 bytes
NekoProxyCore.exe SHA-256: 1b9b0ba313ac1f8c879f07f678a2f01e5b334c29fc17323533017aed2cbffcfe
NekoProxyCore.dll SHA-256: 1703322471e47baf9897a9ee05e8d2600e8ce150a5a61b5c10142d0a6d6c3acb
NekoProxyCore.Core.dll SHA-256: ebb2dde1674aef7c6ef06bc5606dbcc32ab6148da830f7d70dc5202e6377c033
NekoProxyCore.deps.json: present
Named Pipe challenge: passed
Initial status: Stopped
Synthetic start: AuthorizationRequired
Stop: Stopped
Credentials used: false
```

Canonical publish blocker ถูกปิดแล้ว สถานะที่ถูกต้องคือ **source-bound canonical Release publish
และ fail-closed composition locally verified** แต่ยังไม่ใช่ production authorization enabled.
Approved immutable production public-key release/trusted-clock composition, Launcher production
gateway, code-signing certificate, authorized `start -> Running`, renewal/runtime enforcement,
S1 access และ Security/Release approval ยังคง `BLOCKED`.

---

## 1. สรุปสำหรับทีม Core

Backend Integration Team เข้ามาปิดช่องว่างด้าน executable host และ packaging เพื่อไม่ให้โครงการรอการประสานงานระหว่างทีมโดยไม่มีกำหนด ปัจจุบันสามารถ build และ publish `NekoProxyCore.exe` สำหรับ `win-x64` พร้อม runtime files ที่จำเป็น และทีม Launcher สามารถนำ bundle ไปประกอบ transport/lifecycle integration ได้แล้ว

อย่างไรก็ตาม เส้นทาง `start` ยังตั้งใจ **fail closed** และตอบ `AuthorizationRequired` เพราะแม้มี `StrictLaunchPermitVerifier` แล้ว แต่ยังไม่มี approved immutable production public-key allow-list, trusted-clock release composition และ continuous-authorization contract ที่สมบูรณ์ ห้ามแก้ให้ start ผ่านด้วย allow-all verifier, local signer, static secret, cached permit หรือ authorization bypass

สถานะโดยย่อ:

| ขอบเขต | สถานะ | หมายเหตุ |
|---|---|---|
| Headless `NekoProxyCore.exe` | `IMPLEMENTED/VERIFIED` | Build และ publish เป็น `win-x64` ได้จริง |
| Protocol v2 framing/parser | `IMPLEMENTED/VERIFIED` | 4-byte big-endian, bounded UTF-8/JSON และ typed responses |
| Named Pipe host | `IMPLEMENTED/VERIFIED FOR INTEGRATION` | Current-user-only pipe; challenge/start/status/stop dispatch |
| Challenge lifecycle | `IMPLEMENTED/VERIFIED` | 32-byte CSPRNG, 43-char base64url, one outstanding/one use |
| Legacy ProcessMode composition | `IMPLEMENTED/PARTIAL` | Host ประกอบ production legacy engine path แล้ว แต่ authorized successful start ยังทดสอบไม่ได้ |
| Exact target PID/name recheck | `IMPLEMENTED/UNIT VERIFIED` | มี seam และ regression tests ก่อน engine side effect |
| Launch permit verifier | `IMPLEMENTED/UNIT VERIFIED; COMPOSITION BLOCKED` | Strict verifier มีแล้ว แต่ release gate ยังไม่มี approved immutable production key ring/trusted clock จึงคง `AuthorizationRequired` |
| Continuous authorization/renewal | `BLOCKED/TBD` | `s0-rc1` ยังไม่มี Launcher ↔ Core renewal wire/token/runtime semantics |
| Signed production bundle/manifest | `BLOCKED` | ยังไม่มี production signing/public-key release artifacts และ release approval |
| S1 downstream proxy access | `BLOCKED` | ยังไม่มี short-lived/non-reusable runtime-bound access mechanism |

---

## 2. สิ่งที่ทำเสร็จแล้ว

### 2.1 Executable host และ composition root

เพิ่ม/ปรับปรุง:

- `NekoProxyCore.Host/Program.cs`
  - สร้าง executable composition root
  - ใช้ single-instance lease
  - initialize legacy Netch runtime
  - ประกอบ `WindowsProcessResolver` → `ProcessModeController` → `NetchProcessModeEngine`
  - เปิด Named Pipe control server
  - stop แบบ bounded/best effort เมื่อ host ถูกยกเลิกหรือเกิด failure
- `NekoProxyCore.Host/SingleInstanceLease.cs`
  - ป้องกัน host ซ้ำด้วย per-user local mutex
- `NekoProxyCore.Host/HeadlessControlServer.cs`
  - current-user-only Named Pipe
  - bounded frame ขนาดสูงสุดตาม Protocol v2
  - unsigned 4-byte big-endian framing
  - dispatch `challenge`, `start`, `status`, `stop`
- `NekoProxyCore.Host/NekoProxyCore.Host.csproj`
  - output เป็น `NekoProxyCore.exe`
  - target `win-x64`
  - ไม่มี WinForms UI entry path
  - stage `mode/`, `i18n/`, root runtime files และ `bin/` dependencies ตอน publish

### 2.2 Legacy build blocker

แก้ `Netch/Netch.csproj` ให้ headless build ตัด legacy UI resource ออกเมื่อใช้ `HeadlessCoreBuild=true` ทำให้ Core host build ผ่านโดยไม่ต้องใช้ WinForms resource path ที่เคยทำให้ MSBuild ล้มเหลว

### 2.3 Protocol และ security seams

มี implementation/tests สำหรับ:

- strict Protocol v2 request parsing และ code-only response serialization
- challenge response ที่มีเฉพาะ frozen fields
- atomic challenge admission/consumption
- bounded redacted `SensitivePermit`
- target-bound canonical configuration ตาม `s0-rc1`
- canonical synthetic fixture digest:
  `92ac70d0f9b100ba664f2bb205b2c042bc1058f779e94e759822d906ea880871`
- fail-closed `AuthorizationRequiredStartAuthorizer`
- `ChallengePermitStartAuthorizer` seam สำหรับ production verifier
- exact target PID/name recheck ก่อน publish `Starting` และก่อน engine side effect
- sanitized typed errors โดยไม่ส่ง arbitrary exception detail ออก wire

### 2.4 Launcher handoff bundle

Bundle ที่ตรวจและส่งล่าสุด:

- `release/NekoProxyCore-win-x64.zip`
- `release/NekoProxyCore-artifact-manifest.json`
- `release/NekoProxyCore-provenance.json`
- `release/NekoProxyCore-verification.json`
- `release/SHA256SUMS.txt`

```text
File:          NekoProxyCore-win-x64.zip
Size:          13,503,603 bytes
SHA-256:       be6184312655c763a44f740b054693ee7dde34a7b098a7ce7f3c26c4b3377a52
Source commit: ce365ebccf976a752854609d2f42d738bfbfd039
Runtime:       .NET 6 x64 (framework-dependent)
Pipe:          NekoProxyCore.s0-rc1
```

Bundle มีไฟล์ที่ host ต้องใช้ ได้แก่ `mode/`, `i18n/`, `bin/`, `nfdriver.sys`, `tun2socks.bin`, `aiodns.conf` และ `stun.txt`

> Bundle เป็น framework-dependent .NET 6 x64 และต้องส่งทั้ง ZIP ห้ามส่ง EXE เดี่ยว
> executable ยัง unsigned และ production authorization ยัง fail closed

---

## 3. หลักฐานการตรวจล่าสุด

### 3.1 Regression suite

```text
Passed: 103
Failed: 0
Skipped: 0
```

คำสั่งที่ใช้:

```bash
dotnet test Tests/Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo
```

### 3.2 Supporting focused checkpoint ก่อน final full-suite run

ก่อนรัน regression suite 103/103 รอบสุดท้าย มี focused checkpoint แยกต่างหาก โดยใช้
สคริปต์ชั่วคราวชื่อขึ้นต้น `hermes-verify-` ภายใต้ Windows temp directory และลบหลังตรวจเสร็จ ผล:

```text
Focused tests: 28/28 passed
Windows host build: passed, 0 warnings, 0 errors
Publish: passed
Named Pipe challenge: 43 characters
Unauthorised start: AuthorizationRequired
Status after rejected start: Failed
```

การตรวจนี้ใช้ executable จริงและ Named Pipe จริง ไม่ใช่ mock host แต่ตัวเลข 28/28 เป็น
supporting checkpoint ไม่ใช่ test count ล่าสุด; test count canonical คือ 103/103 ในหัวข้อ 3.1

### 3.3 Security contract package validation

Authoritative package:

`Backend Security/security-contract/NEKO-AUTH-S0/s0-rc1/`

ผล `python validate_package.py`:

```text
PASS contractRevision=s0-rc1
PASS files=15
PASS canonicalSha256=92ac70d0f9b100ba664f2bb205b2c042bc1058f779e94e759822d906ea880871
PASS packageSha256=6697351b6b280afc566fedaaa1a6cfe207b1ea1d803c2eb613b4c1a891e192df
PASS syntheticRs256Vector=valid-launch-01
PASS privateKeyMarkers=0
```

สถานะ approval ใน package ปัจจุบัน:

- Backend/Security technical authority: `APPROVED`
- Launcher Owner: `PENDING ACCEPTANCE`
- NekoProxyCore Owner: `PENDING ACCEPTANCE`
- Release authority: `BLOCKED`
- Proxy Server/Security S1: `BLOCKED`

---

## 4. พฤติกรรมที่ Launcher ใช้ประกอบได้แล้ว

```text
Launcher-owned Core process
  → connect Named Pipe: NekoProxyCore.s0-rc1
  → Protocol v2, 4-byte big-endian frame
  → challenge
  → status / stop
  → start with target-bound request and opaque permit
  → Core currently returns AuthorizationRequired (intentional fail closed)
```

ทีม Launcher สามารถทดสอบได้ทันที:

1. spawn และถือ owned process handle
2. ตรวจ Core process lifecycle/single instance
3. connect Named Pipe และทำ framed I/O
4. ขอ challenge และตรวจ correlation/shape
5. ทดสอบ malformed frame, oversized frame, strict JSON และ correlation mismatch
6. เรียก `status`/`stop`
7. ส่ง `start` เพื่อยืนยัน fail-closed path
8. ทดสอบ Core crash, Launcher crash, timeout และ bounded cleanup

ทีม Launcher **ยังทดสอบ `start → Running` จริงไม่ได้** จนกว่าจะมี production verifier/key artifacts และ contract gaps ด้าน renewal ถูกปิด

---

## 5. สิ่งที่ทีม Core ต้องทำต่อ

### P0 — Contract acceptance และ production verifier

1. ตรวจและบันทึกการยอมรับ `NEKO-AUTH-S0/s0-rc1`:
   - revision: `s0-rc1`
   - package SHA-256: `6697351b6b280afc566fedaaa1a6cfe207b1ea1d803c2eb613b4c1a891e192df`
   - consumer revision/commit
   - package validation result
   - Owner decision: `ACCEPT` หรือ `REJECT` พร้อมเหตุผล
2. Review และ complete acceptance evidence ของ `StrictLaunchPermitVerifier : IPermitVerifier` ที่มีแล้วตาม package exact rules:
   - compact JWT สาม segments
   - exact `alg=RS256`, `typ=neko-launch+jwt`, known `kid`
   - reject `crit`, unknown/duplicate header และ claims
   - exact issuer/audience/product/scope
   - strict claim types/bounds/ASCII rules
   - exact `iat`/`nbf`/`exp`, 30-second lifetime และ 2-second skew boundaries
   - bind challenge, canonical configuration digest, mode และ target PID
   - atomic JTI replay protection
   - constant-time comparison เมื่อเหมาะสม
   - unavailable key/clock/verifier ต้อง fail closed
3. Wire `ChallengePermitStartAuthorizer` เป็น production authorizer เฉพาะเมื่อ immutable approved key allow-list พร้อม
4. เพิ่ม positive/negative verifier tests จาก synthetic vectors ทั้งหมดใน contract package
5. พิสูจน์ทุก verification failure ทำให้ engine/network/driver start count เท่ากับศูนย์

### P0 — Runtime authorization

1. รอ contract revision ถัดไปก่อน implement production renewal wire
2. หลัง package ใหม่ออก ให้ implement:
   - renewal challenge request/response
   - signed renewal material verifier
   - runtime ID/config digest binding
   - 15-second renewal cadence และ 30-second signed expiry
   - immediate bounded stop เมื่อ expiry/revocation/backend outage
   - 5-second revocation stop deadline
3. ห้าม reuse `start`/`challenge` fields หรือคิด schema เพิ่มเองระหว่างรอ

### P1 — Host hardening และ integration completion

1. ทบทวน pipe/mutex name และ anti-squatting/process-binding algorithm กับ contract revision ถัดไป
2. เพิ่ม total monotonic deadlines สำหรับ connect/read/write/dispatch ทุก operation
3. กำหนด behavior เมื่อ client ส่ง malformed frame แล้ว disconnect/reconnect ให้ชัดและทดสอบ
4. เพิ่ม graceful host shutdown command/lifecycle policy ที่ไม่ทำให้ engine orphan
5. ทดสอบ single-instance race, concurrent clients, replay, partial frames และ abrupt disconnect
6. รัน authorised successful ProcessMode E2E กับ `pso2.exe` test target/approved fixture เมื่อ verifier พร้อม
7. ตรวจ clean machine โดยติดตั้งเฉพาะ approved prerequisites

### P1 — Packaging/release

1. สร้าง immutable artifact manifest ตาม accepted schema
2. รวม complete runtime file set และปฏิเสธ missing/extra/case-collision/path traversal/reparse points ตาม contract revision ที่อนุมัติ
3. รอ signed release trust anchor และ production key manifest
4. ทำ secret-sentinel scan กับ source, logs, dumps และ published bundle
5. ทดสอบ extracted-bundle/direct-Core bypass
6. ส่งให้ Security/QA/Release ตรวจและลงนามก่อน Go-Live

---

## 6. สิ่งที่กำลังรอจากทีมอื่น

| รายการที่รอ | Owner | เหตุผลที่บล็อก |
|---|---|---|
| Production public-key allow-list/key manifest | Backend/Security + Release | Core ไม่สามารถ verify production launch permit ได้โดยไม่เดาคีย์หรือสร้าง local trust |
| Production signing/attestation trust anchor | Security/Release | Integration bundle ปัจจุบันยัง unsigned และใช้เป็น production artifact ไม่ได้ |
| Contract revision ถัดไปสำหรับ Launcher ↔ Core renewal | Backend/Security contract owner | `s0-rc1` ยังไม่มี renewal command/schema/token/runtime semantics |
| Approved pipe server process-binding semantics | Backend/Security + Launcher/Core | Candidate design ยังไม่อยู่ใน hash-pinned contract package |
| Manifest path-safety semantics | Backend/Security + Release | ต้องออก schema/revision/hashใหม่ก่อน production implementation |
| S1 downstream proxy access | Proxy Server/Security S1 | ห้ามใช้ static reusable proxy credential; เป็น hard release blocker |
| Launcher owner acceptance และ integration evidence | Launcher Team | ต้องพิสูจน์ owned process, pipe binding, timeout, crash และ cleanup |
| Core owner acceptance | NekoProxyCore Team | ต้องรับ revision/hash เดียวกันก่อนถือว่า contract effective |
| Production endpoint/credentials | Backend/Security | ไม่มีใน sanitized package และห้ามใส่ใน source/artifact/log |

---

## 7. Stop rules ระหว่างรอ

ห้ามทำสิ่งต่อไปนี้เพื่อให้ `start` ผ่าน:

- allow-all/no-op permit verifier
- local/offline signer
- static/shared secret
- token-controlled JWKS URL หรือ first-key fallback
- protocol v1 หรือ legacy unauthorised start fallback
- cached permit หรือ retry permit หลัง ambiguous outcome
- ส่ง permit ผ่าน argv, environment, disk, config, log หรือ telemetry
- ขยาย authorization ด้วย Launcher boolean/local heartbeat
- เดา renewal wire/schema ก่อน contract revision ใหม่
- ใช้ S0 launch permit แทน downstream proxy credential ของ S1

Default ที่ถูกต้องระหว่างรอคือ `AuthorizationRequired`/`AuthorizationUnavailable` และ engine start count ต้องเป็นศูนย์

---

## 8. เกณฑ์พร้อมส่ง Production

จะเปลี่ยนสถานะจาก integration-ready เป็น production-ready ได้เมื่อครบทุกข้อ:

- [ ] Core Owner บันทึก acceptance ของ revision/package hash
- [ ] `StrictLaunchPermitVerifier` ผ่าน positive/negative vectors และ replay/time boundary tests
- [ ] Production immutable public-key allow-list ผ่าน signed release control
- [ ] Authorized start เดินถึง `Running` โดยไม่มี bypass และ exact target recheck ผ่าน
- [ ] Contract revision ถัดไปปิด renewal, pipe binding และ manifest path-safety gaps
- [ ] Continuous authorization/expiry/revocation enforcement ผ่าน
- [ ] S1 downstream access mechanism ผ่าน Security acceptance
- [ ] Clean-machine, crash, orphan-process และ anti-bypass E2E ผ่าน
- [ ] Signed artifact manifest/bundle ผ่าน integrity verification
- [ ] ไม่มี secret/permit/credential leakage ใน artifact/log/dump/telemetry
- [ ] Launcher, Core, Backend/Security, Proxy/S1, QA และ Release ลงนาม Go/No-Go

---

## 9. เอกสารอ้างอิง

- [Documentation index](../README.md)
- [Historical archive index](../archive/README.md)
- [Central production adapter handoff](../archive/security-s0/central-production-adapter-handoff.md)
- [Core security implementation handoff](../archive/security-s0/core-security-implementation-handoff.md)
- [Launcher/Core authorization adapter handoff](../archive/security-s0/launcher-core-authorization-adapter-handoff.md)
- [Step E security authorization report](../archive/security-s0/step-e-security-authorization-report.md)
- Backend-owned contract package `NEKO-AUTH-S0/s0-rc1` ต้องตรวจจาก Backend/Security
  repository ที่ owner ระบุ ห้ามใช้ archived Core copy แทน approval ปัจจุบัน

## 10. Handoff decision

**งาน implementation และ canonical packaging ฝั่ง Core ปิดแล้ว** ที่ revision
`ce365ebccf976a752854609d2f42d738bfbfd039`

**ทีม Launcher รับ `release/NekoProxyCore-win-x64.zip` ไปประกอบได้แล้ว** สำหรับ
process/pipe/protocol/fail-closed lifecycle tests

**ยังห้ามประกาศ production-ready หรือ release** จนกว่า P0 blockers และ release gates ในเอกสารนี้ผ่านครบ
