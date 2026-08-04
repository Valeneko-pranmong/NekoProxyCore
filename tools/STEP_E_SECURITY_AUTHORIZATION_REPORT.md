# Step E Security Authorization Report and Implementation Handoff

วันที่จัดทำ: 2026-08-03
ผู้รับผิดชอบปลายทาง: Backend, Security, Launcher, NekoProxyCore และ Proxy Server teams
Repositories ที่ตรวจ:

- Producer/Core: `D:\NekoProxyCore`
- Consumer/Launcher/Backend migrations: `D:\Neko-Family-Proxy`

สถานะเอกสาร: **ACTION REQUIRED — SECURITY BLOCKER FOR STEP E PRODUCTION HOST**

> เอกสารนี้เป็น supporting sanitized threat-model/remediation report สำหรับ authorization boundary ของ Step E ไม่มี endpoint, access token, proxy credential, private key, customer identifier หรือ raw runtime configuration
>
> สถานะรวมและลำดับงาน canonical อยู่ที่ `D:\Audit Neko project\Proxy core to do\README.md`; Step D ในรายงานนี้เป็น historical pre-authorization evidence

---

## 1. Executive summary

Step D พิสูจน์แล้วว่า ProcessMode สามารถทำงานกับ `pso2.exe` จริง ผ่าน local SOCKS, gameplay traffic, server-side TCP/UDP correlation และ cleanup ตาม `tools/PROCESSMODE_TEST_REPORT.md` ผลดังกล่าวยังคงเป็น **PASS** และไม่ถูกเปลี่ยนโดยรายงานนี้

อย่างไรก็ตาม การตรวจ Step E พบช่องว่างด้าน authorization ที่ต้องแก้ก่อนสร้าง production host และเชื่อม Launcher:

1. Launcher มี authentication, entitlement, installation, claimed session และ heartbeat checks จริง แต่ checks เหล่านี้เป็น client-side orchestration ซึ่งผู้ใช้ที่แก้ Python/packaged Launcher สามารถข้ามได้
2. Named pipe แบบ `CurrentUserOnly` ป้องกัน Windows user อื่น แต่โปรแกรมใด ๆ ภายใต้ Windows user คนเดียวกันยังเรียก Core ได้ จึงไม่ใช่ authorization proof
3. Protocol draft ปัจจุบันสามารถส่ง `start` ด้วย opaque references และ `pso2.exe` โดยยังไม่มีหลักฐานที่ Backend เซ็นรับรอง
4. การพบ process ชื่อ `pso2.exe` เป็น activation precondition ไม่ใช่หลักฐานสิทธิ์ใช้บริการ และชื่อ process สามารถถูกเลียนแบบได้โดย local attacker
5. Packaging ปัจจุบันของ Launcher ฝังทุกไฟล์ใต้ `ProxyCore/` หาก runtime bundle มี static reusable Shadowsocks credential ผู้ใช้สามารถแตก bundle แล้วใช้ proxy client อื่นเชื่อมโดยตรง ข้ามทั้ง Launcher และ Core authorization

**Security decision ที่ยืนยันแล้ว:** ทุกการเริ่ม Core ต้องออนไลน์ และ Backend ต้องออก short-lived launch permit เฉพาะหลังตรวจ authentication, account, entitlement/license, installation, active launcher session และ heartbeat ใหม่เรียบร้อยแล้ว

**Overall classification:**

| Boundary | Result |
|---|---|
| Step D ProcessMode/runtime/gameplay | HISTORICAL PASS — pre-authorization evidence |
| Launcher normal-flow authorization | PARTIAL — implemented but client-bypassable |
| Core start authorization | FAIL — no server-verifiable proof yet |
| Same-user IPC isolation | PARTIAL — transport isolation only |
| Direct proxy credential bypass prevention | UNVERIFIED / POTENTIALLY CRITICAL |
| Step E production host implementation authorization | BLOCKED |
| Production release authorization | NOT GRANTED |

---

## 2. Intended security property

ระบบต้องรับประกันตามขอบเขตที่เป็นไปได้ว่า:

1. Core ไม่เริ่ม ProcessMode เพียงเพราะมีผู้เรียก executable หรือ named pipe ได้
2. Backend เป็น authority ของ account, license, installation และ launcher session
3. Launcher เป็นผู้ประสาน flow แต่ไม่ใช่ root of trust ที่ Core เชื่อโดยตรง
4. Core เชื่อเฉพาะ authorization permit ที่ Backend เซ็นและตรวจสอบลายเซ็นได้
5. Permit ใช้ได้กับ Core process/start attempt และ configuration ที่ระบุเพียงครั้งเดียวภายในช่วงเวลาสั้น
6. Core และ Launcher ต้อง fail closed เมื่อ Backend ติดต่อไม่ได้, permit หาย/ผิด/หมดอายุ/ใช้ซ้ำ หรือ target process ไม่ผ่าน
7. Authorization material และ proxy credential ต้องไม่ปรากฏใน argv, environment, log, report, telemetry, crash message หรือ persisted temporary files

Non-goal: ไม่สามารถรับประกัน DRM 100% บนเครื่องที่ผู้ใช้เป็น administrator และควบคุม executable/memory ได้ Code signing, integrity verification, obfuscation และ TPM proof-of-possession เป็น defense-in-depth ไม่ใช่สิ่งทดแทน server authorization

---

## 3. Current architecture and findings

### 3.1 Existing Launcher authorization flow

Launcher ปัจจุบันมีองค์ประกอบที่ถูกต้องสำหรับ normal product flow:

- Supabase authentication
- entitlement/license state
- installation identity/hash
- `claim_session`
- `heartbeat_session`
- `release_session`
- session revocation/takeover behavior

จุดอ้างอิงสำคัญ:

- `launcher/src/neko_launcher/application/controller.py:157-190`
- `launcher/src/neko_launcher/application/services.py:186-208`
- `launcher/src/neko_launcher/infrastructure/supabase_gateway.py:130-188`
- `supabase/migrations/20260725081929_link_launcher_sessions_to_licenses.sql:4-95`

ข้อจำกัด: state เช่น `AUTHENTICATED`, active entitlement และ `session_id` อยู่ใน process ของ Launcher ผู้ใช้ที่แก้หรือแทนที่ Launcher สามารถข้าม conditional branches แล้วเรียก Core โดยตรงได้

### 3.2 Existing Core/IPC boundary

Protocol work-in-progress ตรวจ schema, opaque references และ canonical target name แต่ยังไม่มี server-signed authorization:

- `NekoProxyCore.Host/Protocol/ControlProtocol.cs`
- `NekoProxyCore.Core/HeadlessRuntimeCoordinator.cs`
- `NekoProxyCore.Core/ProcessModeController.cs`

`PipeOptions.CurrentUserOnly` ที่วางแผนไว้มีประโยชน์และต้องคงไว้ แต่ให้ classification เป็น **local transport isolation** เท่านั้น ไม่ใช่ caller authentication

### 3.3 Process-name limitation

Core และ Launcher ต้องตรวจ `pso2.exe` เพื่อรักษา fail-closed activation requirement แต่ process name อย่างเดียวไม่พิสูจน์ว่าเป็น game binary จริง

Defense-in-depth ที่พิจารณาเพิ่มได้:

- canonical executable path ภายใต้ approved installation
- Authenticode publisher/signature verification
- expected binary metadata/hash policy ที่รองรับ game updates
- process ownership/session checks

มาตรการเหล่านี้เพิ่มต้นทุนการ spoof แต่ไม่สามารถต้าน local administrator ได้สมบูรณ์

### 3.4 Static proxy credential bypass

`launcher/NekoLauncher.spec:18-27` ฝังทุกไฟล์ใต้ `ProxyCore/` เข้า one-file distribution หาก settings/runtime มี server address/password/key ที่ reusable ผู้ใช้สามารถ extract แล้วใช้งานผ่าน generic Shadowsocks client โดยไม่เรียก Launcher/Core

นี่เป็นคนละ boundary กับ launch permit:

- launch permit ป้องกัน unauthorized Core start
- server credential/session enforcement ป้องกัน direct proxy use

ห้ามประกาศว่า “กันผู้ใช้แกะไปใช้ proxy ได้” จนกว่า Proxy Server/Backend จะปิด boundary หลังนี้ด้วยหลักฐานจริง

---

## 4. Threat model

### 4.1 Assumed attacker capabilities

ให้ถือว่าผู้ใช้เครื่อง client สามารถ:

- อ่าน/แก้ Python และ PyInstaller bundle
- เปิด Core executable โดยตรง
- เชื่อม named pipe ใน Windows account เดียวกัน
- อ่าน argv, environment และไฟล์ที่แจกไปกับโปรแกรม
- replay request/token ที่ดักได้
- restart Core เพื่อล้าง in-memory state
- คัดลอก installation data หรือ bearer/refresh token ที่ account ของตนเข้าถึงได้
- เรียก public/authenticated Backend APIs โดยไม่ผ่าน official UI

### 4.2 Out-of-scope attacker resistance

ไม่อ้างว่าสามารถต้าน:

- local administrator ที่ patch Core หรือเปลี่ยน embedded public key
- process injection/debugging/memory extraction ระดับสูง
- kernel compromise
- stolen valid account credentials โดยไม่มี additional proof-of-possession

### 4.3 Assets to protect

- สิทธิ์เริ่ม ProcessMode
- proxy server access
- customer entitlement/session policy
- signing private key
- proxy credential
- Supabase access/refresh token
- configuration mapping และ runtime secrets

---

## 5. Required authorization design

### 5.1 Trust boundary

```text
Supabase Auth + Backend authority
  └─ validates account/license/installation/session/heartbeat
       └─ signs one-use short-lived launch permit
            ↓ TLS
Launcher orchestration
  ├─ must observe pso2.exe
  ├─ obtains Core challenge
  └─ forwards challenge to Backend and permit to Core
            ↓ CurrentUserOnly named pipe
NekoProxyCore
  ├─ verifies signature and all permit claims
  ├─ consumes one-use challenge
  ├─ validates canonical configuration binding
  ├─ rechecks pso2.exe
  └─ only then starts runtime/driver/network side effects
```

Core must never trust the following as standalone authorization:

- boolean `authorized=true`
- Launcher process name/path/PID/parent PID
- mutex ownership
- Windows user identity alone
- raw `session_id`
- installation hash
- shared secret embedded in Launcher/Core
- unsigned JSON
- process detection alone

### 5.2 Core-generated challenge

ก่อนขอ permit แต่ละ start attempt:

- Core สร้าง random challenge อย่างน้อย 256 bits จาก cryptographic RNG
- encode แบบ base64url และจำกัดขนาดแน่นอน
- challenge อายุไม่เกิน 30 วินาทีโดยใช้ monotonic deadline ภายใน Core process
- เก็บเฉพาะ memory
- consume แบบ atomic ใน authorization attempt แรก ไม่ว่าผลจะผ่านหรือไม่
- start attempt ใหม่ต้องขอ challenge ใหม่
- challenge ของ Core process เดิมใช้กับ Core process ใหม่ไม่ได้

เหตุผล: ใช้เพียง permit `jti` กับ replay cache ในเครื่องไม่พอ เพราะ attacker restart Core หรือลบ persistent cache ได้

### 5.3 Backend permit issuance request

ตัวอย่าง sanitized contract:

```json
{
  "sessionId": "<uuid>",
  "installationKeyHash": "<64-lowercase-hex>",
  "challenge": "<core-generated-base64url>",
  "command": {
    "protocolVersion": 2,
    "processName": "pso2.exe",
    "profileReference": "profile-0",
    "serverReference": "server-0"
  }
}
```

กฎ:

- authenticated user identity ต้องมาจาก Supabase bearer token เท่านั้น
- ห้ามรับ `userId`, role, license status, entitlement status หรือ `authorized` จาก client เป็นความจริง
- จำกัด request size และ validate ทุก field
- configuration ที่อนุญาตต้องมาจาก allow-list/product policy ฝั่ง Backend ไม่ใช่เชื่อ opaque references อย่างเดียว

### 5.4 Server-side validations

Permit issuance ต้องตรวจแบบ transactionally consistent หรือผ่าน authority function เดียว:

1. access token valid และมี authenticated user
2. profile/account status เป็น active
3. requested product active
4. license เป็นของ user เดียวกัน, active, เริ่มใช้แล้ว และยังไม่หมดอายุ
5. installation เป็นของ user เดียวกันและไม่ถูก revoke
6. launcher session เป็นของ user เดียวกันและไม่ถูก revoke
7. session อ้างถึง installation และ license แถวเดียวกับที่ตรวจ
8. heartbeat ยังสด; แนะนำไม่เกิน 60 วินาที
9. requested configuration ได้รับอนุญาตสำหรับ product/session นี้
10. Backend signing key พร้อมใช้งาน มิฉะนั้น fail closed

การออก permit สำเร็จสามารถ refresh heartbeat แบบ atomic ได้ เท่ากับทุก start ต้อง online และตรวจ session ใหม่จริง

### 5.5 Signed permit claims

แนะนำ RS256 permit โดย private key อยู่เฉพาะ Supabase Edge Function secret หรือ managed signing service และ Core มี public verification key ring เท่านั้น

Required header:

```json
{
  "typ": "JWT",
  "alg": "RS256",
  "kid": "<active-key-id>"
}
```

Required claims:

| Claim | Purpose |
|---|---|
| `iss` | exact trusted issuer |
| `aud` | exact Core launch audience |
| `sub` | authenticated user UUID |
| `sid` | exact launcher session UUID |
| `iid` | exact installation row UUID |
| `lid` | exact license row UUID |
| `product` | exact product code |
| `scope` | exact `proxy:start` permission |
| `cfg` | SHA-256 of canonical security-relevant start configuration |
| `challenge` | Core-generated one-use challenge |
| `jti` | random token identifier อย่างน้อย 128 bits |
| `iat` | issue time |
| `nbf` | not-before time |
| `exp` | expiry time |

Recommended policy:

- permit TTL 30 วินาที; hard maximum 60 วินาที
- clock skew allowance ไม่เกินประมาณ 5 วินาที
- Core บังคับ `exp - iat <= 60` เอง
- exact issuer, audience, scope, product และ algorithm
- reject `alg=none`, algorithm confusion, missing/unknown `kid`, duplicate claims, malformed UUIDs และ oversized token
- token ห้ามอยู่ใน argv, environment, file, log, telemetry, correlation ID หรือ response

### 5.6 Canonical configuration binding

Backend และ Core ต้อง freeze canonical serialization เดียวกัน เช่น:

```text
protocolVersion=2\n
processName=pso2.exe\n
profileReference=profile-0\n
serverReference=server-0\n
```

จากนั้นคำนวณ SHA-256 และใส่ใน `cfg` เพื่อป้องกัน permit ที่ขอสำหรับ profile/server หนึ่งถูกนำไปใช้กับอีก configuration

Shared fixtures ต้องมีทั้ง canonical text และ expected hash แต่ไม่มี credential จริง

### 5.7 Core validation order

ก่อน bootstrap ที่มี side effect, driver activation, child process หรือ network start:

1. parse bounded request/token
2. resolve pinned public key จาก exact `kid`
3. require `alg == RS256` และ verify signature
4. validate `iss`, `aud`, `scope`, product, `iat`, `nbf`, `exp`, maximum lifetime
5. constant-time compare permit challenge กับ outstanding Core challenge
6. recompute และ compare `cfg`
7. atomically consume challenge/authorization attempt
8. validate opaque start request
9. recheck real target process requirement
10. invoke authorized runtime start seam

Missing, invalid, expired, replayed หรือ unauthorized permit ต้องจบก่อน engine start โดย engine start count เป็นศูนย์

Authorization ต้องอยู่ที่ shared innermost production start seam ไม่ใช่เฉพาะ pipe handler มิฉะนั้น integration runner หรือ adapter อื่นอาจเรียก runtime ข้าม gate ได้ Production build ต้องไม่มี debug/offline/allow-if-missing fallback

---

## 6. Direct proxy access remediation

Backend, Security และ Proxy Server teams ต้องเลือกรูปแบบที่ไม่แจก static reusable credential อย่างน้อยหนึ่งแบบ:

### Preferred

- Backend ออก per-user/per-session proxy access material อายุสั้น
- ผูกกับ active license/session/installation
- revoke/expire ได้ server-side
- Core รับผ่าน protected in-memory channel หลัง permit validation
- credential ไม่ถูก pack ใน `ProxyCore/` และไม่ถูกเขียน disk/log

### Other acceptable architectures subject to Security approval

- authenticated access gateway ที่ตรวจ entitlement ก่อน forward ไป Shadowsocks
- per-session server credential พร้อม expiry/revocation
- server-side account/plugin enforcement ที่ไม่ใช้ shared static password

### Not acceptable as a security control

- ซ่อนหรือ obfuscate static password
- pack credential ใน PyInstaller แล้วถือว่า extract ไม่ได้
- encrypt credential ด้วย key ที่ฝังใน client เดียวกัน
- rename settings file
- rely on Launcher UI checks

หาก server stack ปัจจุบันรองรับเฉพาะ shared Shadowsocks password ให้จัด classification เป็น **BLOCKED/PARTIAL** ไม่ใช่ PASS และสร้าง Server migration plan แยกต่างหาก

---

## 7. Continuous authorization decision

Launch permit อนุญาตเฉพาะ start event การ revoke session หลัง Core Running จะไม่หยุด Core ทันทีหาก heartbeat อยู่เฉพาะ Launcher ซึ่ง attacker สามารถ patch ได้

Backend/Security ต้องตัดสินใจอย่างชัดเจน:

### Minimum approved policy

- ทุก start online-authorized
- Core หยุดเมื่อ `pso2.exe` ปิด
- session/credential ฝั่ง server expire/revoke ได้ในเวลาจำกัด

### Stronger recommended policy

- Core ขอ signed renewal โดยตรงหรือผ่าน challenge-response ทุกช่วงเวลาสั้น
- renewal ผูกกับ active runtime/session
- fail closed หลัง bounded grace period
- Launcher shutdown/revocation เป็น signal เพิ่มเติม ไม่ใช่ authority เดียว

จนกว่าจะตัดสิน continuous renewal ห้ามอ้างว่า session revocation หยุด running Core ได้ทันที

---

## 8. Key management and rotation

1. Private signing key อยู่เฉพาะ Edge Function secret/managed signer
2. ห้าม commit `.pem`, `.key`, `.pfx`, environment secret หรือ private JWK
3. Core ฝัง public-key ring ขนาดเล็กและเลือก exact key ด้วย `kid`
4. Unknown `kid` ต้อง reject ห้าม fallback ไป key อื่น
5. Rotation ปกติ:
   - ship Core ที่รู้จัก old + new public keys
   - เริ่ม sign ด้วย new private key
   - เก็บ old verification keyอย่างน้อย maximum TTL + skew
   - ลบ old key ใน release ถัดไป
6. Emergency remote rotation ต้องใช้ key manifest ที่ signed ด้วย offline root key ซึ่งฝัง public root ใน Core; ห้าม trust unsigned JWKS โดยอัตโนมัติ
7. บันทึกเฉพาะ key ID, permit result/error code และ aggregate counts ห้ามบันทึก token

---

## 9. Required typed errors

เพิ่ม error taxonomy ที่ sanitized และไม่เปิดเผย validation detail เกินจำเป็น เช่น:

- `AuthorizationRequired`
- `AuthorizationInvalid`
- `AuthorizationExpired`
- `AuthorizationReplay`
- `AuthorizationUnavailable`
- `SessionInactive`
- `ProcessNotFound`
- `ProcessExited`
- existing lifecycle timeout/cancel/start/stop errors

Wire response ต้องมีเฉพาะ protocol version, kind, correlation ID, typed status, success boolean และ error code ไม่มี exception message/token/claim/user/session/license/installation identifier

---

## 10. Team ownership and required actions

| Team | Required action | Gate evidence |
|---|---|---|
| Backend | สร้าง authenticated permit issuance boundary และ authority query/RPC | unauthorized/revoked/expired/stale cases fail closed |
| Backend | ออก RS256 permit ที่ผูก Core challenge + config hash | cross-language fixtures and signature vectors |
| Security | อนุมัติ claims, TTL, clock skew, replay semantics, key custody/rotation | signed threat-model review |
| Security | ตัดสิน continuous authorization policy | documented revocation SLA |
| Core | เพิ่ม Core challenge endpoint/state และ strict verifier | engine start count 0 สำหรับทุก auth failure |
| Core | วาง authorization ที่ innermost production start seam | alternate entry-point bypass tests |
| Launcher | ขอ challengeหลังพบ `pso2.exe`, ขอ permit online แล้วส่งผ่าน bounded pipe | no permit in argv/env/log/disk |
| Launcher | ไม่ถือ `Popen` เป็น readiness; รอ typed `Running` | timeout/crash/correlation tests |
| Proxy Server | ยกเลิก static reusable client credential หรือเพิ่ม server enforcement | extracted bundle cannot obtain reusable access |
| Release | code-sign Launcher/Core และตรวจ artifact integrity | signed artifact/hash/clean-machine evidence |
| QA | real authorized and unauthorized E2E gates | sanitized exit/status/orphan evidence |

---

## 11. Required security tests

### Backend

- unauthenticated request
- invalid/expired access token
- suspended/banned account
- inactive product
- absent/expired/revoked/suspended license
- wrong user/session/installation/license relationship
- revoked installation
- revoked/stale/taken-over session
- malformed/oversized challenge/config
- signing-key unavailable
- permit TTL/claims/header exactly match contract

### Core

- missing permit
- malformed token/signature
- `alg=none` and wrong algorithm
- unknown/missing `kid`
- wrong issuer/audience/scope/product
- expired/not-yet-valid/excess-lifetime token
- wrong/reused/expired challenge
- wrong configuration hash
- duplicate/invalid claims
- concurrent double start
- process absent after valid permit
- alternate entry point cannot bypass authorizer
- every failure has engine start count 0
- permit sentinel absent from response/log/temp/crash artifacts

### Launcher

- no Core start before exact `pso2.exe` detection
- detector failure/timeout fails closed
- cannot start without challenge and online permit
- Backend timeout/failure fails closed
- permit remains off argv/environment/disk/log
- `Popen` is not Running readiness
- graceful stop then owned-process kill fallback only after bounded timeout
- repeated target detection/start does not create duplicate host/runtime

### Proxy credential/package

- enumerate actual publish/bundle files dynamically
- extract the distributable in an isolated test directory
- scan schema/filenames/content safely without printing secret values
- prove no static reusable server credential is present
- prove server rejects access after session/credential expiry or revocation

### Real integration

- no `pso2.exe`: no Core/proxy/driver activation
- valid target but no permit: engine start count 0
- valid permit but no target: engine start count 0
- valid authorization + real target: typed `Running`
- gameplay traffic correlation remains valid
- target exit/revocation policy causes bounded cleanupตาม approved design
- no orphan Core/helper/pipe/mutex/controller/temp state

---

## 12. Revised Step E implementation plan

### E0 — Security architecture freeze

- freeze online-only policy
- freeze Core challenge and signed permit contract
- freeze server-side authority query
- decide continuous renewal/revocation SLA
- decide non-reusable proxy access architecture
- Security approval requiredก่อน production host integration

### E1 — Protocol and non-UI bootstrap

- revise IPC protocol to include `challenge` flow and mandatory permit
- implement bounded, versioned, allow-listed JSON frames
- add strict authorization error taxonomy
- retain shared non-UI runtime bootstrap
- keep the checkpoint Host project as a protocol library; it must not expose a runnable production entry point before authorization is implemented
- tests must prove unauthorized start never constructs/starts runtime

Current development evidence: focused protocol/bootstrap tests passed `12/12`, but those tests predate mandatory permit behavior and therefore are not an authorization PASS

### E2 — Production headless host

- `NekoProxyCore.exe` as `WinExe`, x64, no console/form/tray/notification
- current-user named pipe and single-instance lease
- Core-generated one-use challenge
- pinned-key permit verifier at innermost production start seam
- deterministic bounded stop/cleanup
- publish/RID/static/headless smoke gates

### E3 — Launcher boundary

- exact `pso2.exe` detection before host activation flow
- request Core challenge
- obtain online Backend permit under current authenticated session
- send permit only in bounded pipe frame
- wait typed `Running`, never infer readiness from `Popen`
- stop on game/Launcher/session policy according to approved revocation design
- cross-repository protocol fixtures

### E4 — Real security and gameplay gate

1. pre-target: no Core/proxy activation
2. target present but unauthorized: fail closed
3. authorized start: one Core and one runtime session
4. no UI/console/tray
5. local SOCKS and gameplay traffic verification
6. external sanitized TCP/UDP correlation
7. expiry/replay/revocation testตาม approved policy
8. target exit, Launcher exit และ host crash cleanup
9. package extraction/direct-proxy-access gate
10. sanitized final PASS/FAIL/BLOCKED report

Signing, installer และ clean-machine release approval remain downstream release gates, but removal of reusable proxy credentials is a mandatory security prerequisite rather than an optional packaging improvement

---

## 13. Acceptance criteria for closing this report

รายงานนี้ปิดได้เมื่อมี primary evidence ครบทุกข้อ:

- [ ] Backend permit issuance implemented and deployed in an approved environment
- [ ] Backend validates account/product/license/installation/session/heartbeat server-side
- [ ] private signing key exists only in approved secret custody
- [x] Core challenge primitive is cryptographically random, one-use and bounded (internal checkpoint; protocol/config/permit binding ยังไม่ complete)
- [ ] Core strictly verifies signature/claims/time/challenge/config hash
- [ ] every authorization failure produces engine start count 0
- [ ] no production alternate entry point bypasses authorization
- [ ] Launcher requires exact target detection and online permit
- [ ] permit/token absent from argv/env/log/disk/report
- [ ] continuous authorization/revocation SLA is documented and tested
- [ ] distributable contains no static reusable proxy credential, or server-side enforcement prevents direct reuse
- [ ] authorized and unauthorized real integration gates pass
- [ ] Security signs off the residual-risk statement

Until all mandatory items are evidenced, Step E security classification remains **BLOCKED/PARTIAL** and production release remains **NOT GRANTED**

---

## 14. Residual risk statement

เมื่อ remediation ข้างต้นผ่าน ระบบจะป้องกัน modified Launcher หรือ arbitrary same-user pipe client จากการสร้าง Backend authorization เอง และลด replay ด้วย Core-generated challenge แต่ยังไม่ทำให้ client machine เป็น trusted environment ผู้ใช้ที่มี local administrator, valid paid account/token หรือความสามารถ patch/inject executable ยังเพิ่มความเสี่ยงได้

เป้าหมายที่สมเหตุสมผลคือ:

- server remains the entitlement authority
- no reusable secret is distributed unnecessarily
- every start is freshly authorized online
- replay and normal client bypass fail closed
- revocation has a documented bounded SLA
- tampering cost is increased through signing/integrity controls
- evidence and logs remain sanitized

ไม่ควรใช้ถ้อยคำว่า “ป้องกันการแกะได้ 100%” ใน product, security หรือ release documentation
