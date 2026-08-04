# Production Adapters: Launcher → NekoProxyCore

**สถานะเอกสาร:** Implementation handoff / ยังไม่อนุญาตให้เปิด production wiring

**ขอบเขต:** การเชื่อมต่อระหว่าง Launcher, Backend Authorization และ NekoProxyCore

**สถานะภาพรวม:** `BLOCKED / IMPLEMENTATION REQUIRED`

> เอกสารนี้อธิบาย adapter ที่ต้องมีสำหรับ production โดยไม่บันทึก endpoint จริง, access token, permit, private key, proxy credential, customer identifier หรือค่าคอนฟิกลับ

## 1. สรุปสถานะปัจจุบัน

Technical baseline ของระบบ Authorization กลางกำหนดแนวทางไว้แล้ว ได้แก่ Protocol v2, permit แบบ JWT/RS256, strict claims/time/replay validation และ shared fixtures อย่างไรก็ตาม การเชื่อมต่อจริงยังไม่พร้อมจนกว่า Launcher Owner และ NekoProxyCore Owner จะรับรอง contract revision/hash เดียวกัน และแต่ละทีม implement production adapters ครบ

ฝั่ง Launcher ปัจจุบันมี abstraction/scaffold สำหรับ orchestration แต่ production composition ยังต้อง fail closed ด้วย `AuthorizationPendingProxyGateway` จนกว่าจะผ่าน production wiring gate

ฝั่ง repository `NekoProxyCore` บน branch `main` ปัจจุบันยังเป็นโปรแกรม Netch แบบ WinForms และยังไม่มีองค์ประกอบ production ต่อไปนี้:

- Protocol v2 headless host;
- current-user-only Named Pipe control server;
- Core challenge endpoint และ one-use challenge store;
- strict RS256 permit verifier และ key resolver;
- authorized start boundary ที่บังคับ permit ก่อน engine/network/driver side effects;
- production artifact manifest สำหรับ Launcher ตรวจสอบก่อน spawn

ดังนั้น ห้ามถือว่าระบบ production-ready จากการที่ login สำเร็จ, Core process เริ่มได้, Named Pipe เชื่อมได้ หรือ unit test ของ scaffold ผ่าน

## 2. Trust boundaries

```text
Launcher UI / Session
  │
  ├─ Local access context + fresh heartbeat
  │
  ├─ CoreProcessAdapter ───────────────► NekoProxyCore headless host
  │                                          │
  ├─ CoreControlChannel ── Named Pipe ───────┤ challenge/start/status/stop
  │                                          │
  └─ LaunchPermitGateway ── HTTPS ─────► Backend Authorization
                                             │
                                             └─ RS256 launch permit
```

หลักสำคัญ:

1. Backend เป็น authorization authority และถือ private signing key
2. Launcher เป็น orchestrator และส่ง permit ต่อแบบ opaque เท่านั้น
3. Core ต้องตรวจ permit ด้วยตัวเองก่อนสร้าง runtime side effect
4. Named Pipe ACL เป็น defense in depth ไม่ใช่ตัวแทน authorization
5. การตรวจ local state ใน Launcher ช่วย fail fast แต่ไม่แทน server-side authority

## 3. Adapter ที่ต้อง implement

### 3.1 `CoreProcessAdapter` — Launcher-owned

**หน้าที่**

- ตรวจ production artifact manifest ก่อนเริ่ม Core
- spawn เฉพาะ executable/RID/version ที่ได้รับอนุมัติ
- เริ่ม Core ด้วย fixed argument list โดยไม่ใช้ shell
- รอ typed control-channel readiness ภายใน bounded timeout
- ถือ ownership ของ process ที่ตัวเองสร้าง
- graceful stop และ kill เฉพาะ owned process เมื่อ timeout
- ป้องกัน orphan process หลัง failure/cancellation/Launcher exit

**Interface boundary**

```text
start_host_without_secrets() -> OwnedCoreProcess
wait_for_control_channel(timeout) -> None
stop_gracefully(timeout) -> StopResult
kill_owned_process_after_timeout() -> None
```

**ข้อบังคับด้านความปลอดภัย**

- ห้ามส่ง access token, permit, proxy credential หรือ raw proxy config ผ่าน argv
- ห้ามส่งข้อมูลดังกล่าวผ่าน environment variables, temporary files หรือ working-directory artifacts
- ต้องใช้ absolute path ที่ผ่าน manifest/hash validation
- ต้อง authenticate manifest ก่อนเชื่อถือ hash หรือ metadata ภายใน ด้วย signature จาก release key ที่ Launcher pin/allow-list ไว้ หรือ trusted manifest hash ที่ฝังมากับ Launcher release
- ต้องตรวจ executable และ dependency set ครบก่อน spawn
- ต้อง fail closed เมื่อ manifest authentication ล้มเหลว, ไฟล์ขาด, มีไฟล์เกิน contract, hash ไม่ตรง, RID ไม่ตรง หรือ signature policy ไม่ผ่าน
- process/PID survival ไม่ถือเป็น `Running`

**Error ที่ควรส่งออก**

ส่งเฉพาะ typed/allow-listed condition เช่น `ArtifactInvalid`, `CoreStartFailed`, `ControlChannelUnavailable`, `Timeout` และ `Cancelled` โดยห้ามส่ง raw exception/command line/path ลับไป UI หรือ telemetry

**Acceptance tests**

- manifest missing/malformed/hash mismatch → ไม่ spawn
- executable/RID ผิด → ไม่ spawn
- cancellation ก่อน spawn → ไม่มี process side effect
- spawn สำเร็จแต่ pipe ไม่พร้อม → bounded cleanup และไม่มี orphan
- adapter throw ระหว่าง partial start → cleanup ยังทำงาน
- secret sentinel ไม่ปรากฏใน argv/env/log/temp/crash artifact

### 3.2 `CoreControlChannel` — Launcher client + Core server

**หน้าที่**

- สื่อสาร Protocol v2 ผ่าน Windows Named Pipe แบบ current-user-only
- รองรับคำสั่ง `challenge`, `start`, `status`, `stop`
- enforce bounded frame size, strict UTF-8 และ strict JSON schema
- สร้าง/ตรวจ correlation ID ต่อ operation
- ส่ง permit ลง direct pipe buffer เฉพาะตอน serialize `start`
- ยอมรับ readiness เฉพาะ typed response `Running`

**Interface boundary ฝั่ง Launcher**

```text
request_challenge(correlation_id, timeout) -> CoreChallenge
start_authorized(command, permit, correlation_id, timeout) -> CoreStatus
get_status(correlation_id, timeout) -> CoreStatus
stop(correlation_id, timeout) -> CoreStatus
```

**Transport rules**

- framing: 4-byte unsigned big-endian length + UTF-8 JSON payload
- reject zero-length, oversized, truncated หรือ malformed frame ก่อน dispatch
- reject UTF-8 BOM, malformed UTF-8, duplicate fields, unknown fields และ wrong types
- field names, command values และ status values ต้อง case-sensitive
- partial reads/writes ต้องใช้ total operation deadline ไม่ reset timeout ทุก chunk
- response correlation ID ต้องตรง request
- pipe name, mutex name, frame limits และ exact schemas ต้องมาจาก approved contract revision

**ข้อบังคับด้านความปลอดภัย**

- permit ห้ามอยู่ใน log, exception, UI, telemetry หรือ response
- ห้าม retry `start` เดิมหลัง timeout/disconnect เพราะ outcome อาจกำกวม
- challenge และ permit ของ attempt ที่ล้มเหลวห้ามนำกลับมาใช้
- Named Pipe ACL ต้องจำกัด current user และ Core ต้องตรวจ authorization แม้ client ต่อ pipe ได้
- Protocol v1 หรือ start command ที่ไม่มี permit ต้องถูกปฏิเสธใน production host

**Acceptance tests**

- unauthorized pipe client → engine start count เท่ากับศูนย์
- malformed/oversized/duplicate/unknown-field frame → typed reject, ไม่มี runtime side effect
- correlation mismatch → fail closed
- partial frame + timeout → bounded failure
- disconnect หลังส่ง start → ไม่ retransmit permit/start เดิม
- valid response ที่ไม่ใช่ `Running` → Launcher ไม่รายงานว่าสำเร็จ
- permit sentinel ไม่หลุด response/log/temp/crash artifacts

### 3.3 `LaunchPermitGateway` — Launcher-owned client, Backend-owned authority

**หน้าที่ฝั่ง Launcher**

- ใช้ authenticated HTTP infrastructure ที่มีอยู่
- ขอ permit หลังได้รับ fresh Core challenge เท่านั้น
- ส่ง session/installation identity, challenge และ canonical command binding ตาม contract
- รับ permit เป็น `OpaquePermit`
- map HTTP/backend failures เป็น typed allow-listed errors
- ใช้ bounded timeout และ fail closed

**Interface boundary**

```text
issue_launch_permit(
  session_id,
  installation_key_hash,
  challenge,
  command,
  timeout
) -> OpaquePermit
```

**หน้าที่ฝั่ง Backend**

- resolve user จาก bearer token; ห้ามเชื่อ user ID ที่ client ส่งมาเอง
- ตรวจ account, product, license/entitlement, installation, claimed session และ fresh heartbeat
- ตรวจ challenge/configuration binding และ request limits
- ออก short-lived, one-use RS256 permit ตาม frozen claims/header/time policy
- เก็บ private key ใน approved server-side secret custody เท่านั้น
- fail closed เมื่อ database, signer, key service หรือ authority dependency ไม่พร้อม
- rate-limit และบันทึกเฉพาะ sanitized outcome/key ID/aggregate metrics

**`OpaquePermit` requirements**

- `repr`/`str`/exception ต้องแสดงเป็น redacted
- เปิดค่าได้เฉพาะ direct transport serialization boundary
- ห้าม decode, refresh, merge claims, re-sign หรือ persist ใน Launcher
- ห้ามเก็บใน clipboard, config, cache, keyring, crash report หรือ analytics

**Acceptance tests**

- no challenge → ไม่มี Backend permit request
- inactive entitlement/session/install/heartbeat → ไม่มี permit
- Backend 401/403/429/5xx/timeout/malformed body → typed failure และไม่ส่ง Core start
- signer unavailable → fail closed
- permit response เกินขนาด/ชนิดผิด → reject
- retry ใหม่ต้องขอ challenge และ permit ใหม่
- bearer-token/permit sentinel ไม่ปรากฏใน log/UI/telemetry

### 3.4 `FreshHeartbeatLaunchPrecondition` — Launcher client + Backend authority

**หน้าที่**

- ทำ online heartbeat ใหม่สำหรับทุก start attempt
- ผูกกับ authenticated session และ installation identity
- block Core spawn เมื่อ heartbeat false, exception, timeout หรือ cancellation
- ไม่ใช้ timestamp ความสำเร็จเก่าแทน fresh probe ของ attempt ปัจจุบัน

**Acceptance tests**

- heartbeat false/exception/timeout → Core process, challenge และ permit side effects เท่ากับศูนย์
- cancellation ระหว่าง heartbeat → ไม่ spawn Core
- success timestamp เดิม + probe ใหม่ล้มเหลว → ต้อง reject
- target exit หลัง heartbeat → ไม่ spawn Core

### 3.5 `AuthorizedCoreOrchestrator` — Launcher-owned composition

Adapter ตัวนี้ประกอบ production flow และบังคับลำดับเดียวที่อนุญาต:

```text
single-flight lock
→ validate opaque command + local access context
→ wait exact pso2.exe and retain target identity
→ fresh online heartbeat
→ recheck same target
→ validate artifact manifest and spawn Core without secrets
→ wait bounded control-channel readiness
→ recheck target
→ request one fresh challenge
→ recheck target
→ request one Backend permit
→ check cancellation and target
→ send authorized start exactly once
→ accept only typed Running
→ monitor target/Core/session
→ bounded stop and owned-process cleanup
```

**ข้อบังคับ**

- concurrent/duplicate start ต้องถูก reject ก่อนเกิด side effect รอบที่สอง
- recheck target identity ต้องตรวจว่าเป็น process เดิม ไม่ใช่เพียงชื่อไฟล์เหมือนกัน
- failure หลัง host-start attempt ต้องเข้า cleanup เสมอ
- adapter exception ต้องถูกลดเป็น public typed error; ห้ามเผย `str(exc)`
- cleanup exception ห้ามกลบ root public failure
- production dependency injection ต้องใช้ adapters จริงทั้งหมด; fake adapters ใช้เฉพาะ test

### 3.6 Core-side `ChallengeStore` — NekoProxyCore-owned

**หน้าที่**

- สร้าง cryptographic challenge ด้วย CSPRNG ตาม contract
- bound representation และ lifetime
- ผูก challenge กับ attempt/context ที่จำเป็น
- consume แบบ atomic one-use รวม failure path
- ป้องกัน replay และ concurrent double-use
- ใช้ monotonic clock สำหรับ local expiry decision

**Acceptance tests**

- empty/malformed/expired challenge → engine start count 0
- challenge reuse หลัง success หรือ failure → reject
- concurrent double-use → สำเร็จได้สูงสุดหนึ่ง request
- unknown challenge → reject โดยไม่เผย validation detail

### 3.7 Core-side `LaunchPermitVerifier` และ `SigningKeyResolver` — NekoProxyCore/Security-owned

**หน้าที่**

- resolve public key จาก exact approved `kid`
- ยอมรับเฉพาะ algorithm/header/claims ที่ frozen
- verify RS256 signature แบบ strict
- ตรวจ issuer, audience, product, scope, subject/session/installation binding
- ตรวจ `iat`, `nbf`, `exp`, TTL และ clock skew ตาม contract
- ตรวจ JWT/claim type, duplicate claim, size และ encoding
- compare challenge/config hash แบบ constant-time
- reject replay ก่อน engine/network/driver side effects

**ข้อบังคับ**

- ห้าม algorithm fallback หรือ trust key URL/JWKS ที่ token เป็นผู้กำหนด
- unknown `kid`, wrong algorithm, missing/duplicate/wrong-type claim ต้อง fail closed
- public-key rotation/retirement ต้องเป็น allow-listed configuration พร้อม change control
- verifier error response ต้องเป็น typed code เท่านั้น ไม่มี raw expected/actual claims

**Acceptance tests**

ครอบคลุมอย่างน้อย: malformed token, wrong algorithm, unknown key, bad signature, missing/duplicate/wrong-type claim, wrong issuer/audience/product/scope, expired/not-yet-valid/excessive TTL, wrong challenge, wrong configuration hash, replay และ concurrent double-use โดยทุก failure ต้องมี engine start count เท่ากับศูนย์

### 3.8 Core-side `AuthorizedStartBoundary` — NekoProxyCore-owned

**หน้าที่**

วาง authorization check ที่ innermost start seam ก่อนเรียก controller/engine/driver/network side effects ทุกทางเข้า ไม่ใช่ตรวจเฉพาะ protocol handler ชั้นนอก

**ข้อบังคับ**

- default behavior เมื่อไม่มี verifier/permit คือ `AuthorizationRequired`
- ทุก alternate entry point ต้องผ่าน boundary เดียวกัน
- final exact `pso2.exe` target check ต้องเกิดก่อน runtime activation
- configuration ที่จะเริ่มจริงต้อง hash ตรงกับ permit binding
- successful authorized attempt ต้องสร้าง runtime ได้ exactly once
- ห้ามมี allow-all, offline permit, local signing หรือ debug bypass ใน production build

### 3.9 `ProductionArtifactManifest` — Core producer + Launcher consumer

Core build/release ต้องส่ง immutable bundle และ manifest ที่ Launcher ตรวจได้ก่อน spawn โดย manifest อย่างน้อยควรระบุ:

- contract revision และ contract hash
- Core semantic/build version
- target RID/architecture
- executable basename
- ordered complete dependency list
- SHA-256 ของทุกไฟล์
- manifest authenticity metadata เช่น signature algorithm และ signing-key ID
- code-signing identity/policy เมื่อ Security freeze แล้ว

Launcher ต้อง authenticate manifest ด้วย release key ที่ pin/allow-list ไว้ หรือเปรียบเทียบ manifest hash กับค่าที่ trusted Launcher release ฝังไว้ก่อนใช้ metadata หรือ file hashes ใด ๆ; ห้ามเชื่อ unsigned manifest ที่อยู่ข้าง artifact เพียงเพราะ parse ได้

Exact basename, pipe/mutex identity, fixed arguments, canonical JSON encoding และ extra-file policy ต้องมาจาก revision ที่ทั้งสองทีมอนุมัติ ห้ามเดาค่าจาก legacy Netch packaging

## 4. Production composition

Production wiring จะเปิดได้เมื่อ dependency graph เป็น adapters จริงครบ:

```text
AuthorizedCoreOrchestrator(
  process = WindowsCoreProcessAdapter(approved_manifest),
  channel = NamedPipeCoreControlChannel(protocol_v2_contract),
  permits = BackendLaunchPermitGateway(authenticated_http_client),
  launch_precondition = BackendFreshHeartbeatPrecondition(...),
  detector = ExactPso2ProcessTargetDetector(...),
  timeouts = ApprovedTimeouts(...),
)
```

ก่อนครบ gate ต้องคง unavailable/fail-closed composition และห้าม fallback ไป start proxy ด้วย local configuration โดยตรง

## 5. Error mapping

Adapter ทุกตัวต้องคืน error แบบ typed และ allow-listed เช่น:

- `AuthorizationRequired`
- `AuthorizationInvalid`
- `AuthorizationExpired`
- `AuthorizationReplay`
- `AuthorizationUnavailable`
- `SessionInactive`
- `TargetUnavailable`
- `TargetExited`
- `ProtocolInvalid`
- `ArtifactInvalid`
- `AlreadyRunning`
- `Timeout`
- `Cancelled`
- `StartFailed`
- `StopFailed`

รายการ exact code และ UI mapping ต้องตรง frozen schema ห้ามเผย raw exception, stack trace, HTTP body, pipe payload, token fragment, path ลับ หรือ expected claim

## 6. Continuous authorization และ stop policy

หลังเข้าสู่ `Running` แล้ว ต้องมี policy ที่ Security/Backend/Core อนุมัติสำหรับ:

- session heartbeat/renewal cadence
- entitlement/session revocation SLA
- grace period เมื่อ Backend ชั่วคราวไม่พร้อม
- target exit และ Launcher exit behavior
- Core crash recovery
- proxy-access material expiry/renewal

หากยังไม่มี policy ที่ freeze แล้ว production release ต้องถือว่า blocked ห้ามสร้าง offline grace หรือ reconnect behavior เอง

## 7. Implementation order

1. **Contract acceptance** — Launcher/Core รับรอง revision/hash และ fixtures เดียวกับ Backend/Security
2. **Core verifier slice** — challenge store, strict verifier, replay protection, engine-side zero-effect tests
3. **Core Protocol v2 host** — headless Named Pipe host พร้อม typed responses
4. **Core release bundle** — immutable x64 artifact + complete manifest
5. **Launcher process/channel adapters** — manifest validation, process ownership, strict pipe client
6. **Backend permit + heartbeat adapters** — authenticated production endpoint integration
7. **Production composition** — แทนที่ `AuthorizationPendingProxyGateway` หลัง gates ผ่านเท่านั้น
8. **Cross-repository E2E** — clean artifacts, approved environment และ real Backend authority
9. **Security/QA sign-off** — residual risks, revocation, secrecy และ release approval

## 8. Cross-repository release gate

Production release ต้องไม่ผ่านจนกว่าผลจริงจาก ship artifacts ยืนยันครบ:

- no target → ไม่มี Core/proxy/driver activation
- target present แต่ไม่มี permit → engine start count 0
- invalid/expired/replayed/config-mismatched permit → engine start count 0
- valid permit แต่ target หายก่อน final Core check → engine start count 0
- valid target + valid permit → exactly one runtime และ typed `Running`
- timeout/disconnect/cancellation → bounded cleanup และไม่มี orphan
- Launcher exit, target exit, Core crash และ session revocation เป็นไปตาม frozen policy
- ไม่มี token/permit/proxy credential sentinel ใน argv/env/files/logs/UI/telemetry/temp/crash/package
- release bundle ไม่มี static reusable proxy credential หรือ direct-proxy bypass
- Launcher, Core, Backend, Security และ QA ลงนาม revision/hash และผลทดสอบเดียวกัน

## 9. Definition of Done ราย adapter

Adapter จะนับว่า production-ready เฉพาะเมื่อ:

- implementation จริงถูก wire ใน composition ที่จะ ship
- unit/contract/negative/security tests ผ่าน
- timeout/cancellation/cleanup ถูกทดสอบด้วย failure injection
- typed error และ secrecy gates ผ่าน
- มี artifact หรือ endpoint handle ที่ตรวจสอบย้อนกลับได้
- contract revision/hash ตรงกันทุก repository
- ไม่มี fake, allow-all, local signer หรือ offline bypass ใน production path

## 10. สิ่งที่ห้ามทำ

- ห้ามฝัง Backend private key หรือ service-role key ใน Launcher/Core
- ห้ามสร้าง permit ใน client หรือเพิ่ม local signing fallback
- ห้ามส่ง permit/token/credential ผ่าน argv, environment, file หรือ log
- ห้ามเริ่ม Core ก่อน exact target + fresh heartbeat
- ห้ามขอ permit ก่อน Core challenge
- ห้าม decode/modify/persist opaque permit ใน Launcher
- ห้าม reuse challenge/permit หลัง failure หรือ ambiguous outcome
- ห้ามถือว่า process/pipe/PID เท่ากับ `Running`
- ห้ามพึ่ง Named Pipe ACL อย่างเดียวโดยไม่ verify permit ใน Core
- ห้าม bypass innermost authorization boundary ผ่าน UI, CLI, tests หรือ legacy start path
- ห้ามประกาศ production-ready จาก scaffold/unit tests โดยไม่มี real authorized E2E

## 11. Owner checklist

### Launcher

- [ ] Implement production `CoreProcessAdapter`
- [ ] Implement strict Named Pipe `CoreControlChannel`
- [ ] Implement production `LaunchPermitGateway`
- [ ] Implement fresh heartbeat precondition
- [ ] Wire orchestrator และ remove unavailable gateway เฉพาะหลัง gate ผ่าน
- [ ] Pass secrecy, cleanup และ ambiguous-outcome tests

### NekoProxyCore

- [ ] Add headless x64 production host
- [ ] Add current-user-only Protocol v2 Named Pipe server
- [ ] Add cryptographic one-use challenge store
- [ ] Add strict RS256 verifier/key resolver/replay protection
- [ ] Place authorization at innermost runtime start boundary
- [ ] Disable/reject unauthorized legacy start paths
- [ ] Publish complete immutable artifact manifest

### Backend/Security

- [ ] Publish authenticated permit issuance contract
- [ ] Enforce server-side account/entitlement/install/session/heartbeat checks
- [ ] Operate approved signing-key custody and rotation
- [ ] Freeze claims/time/replay/revocation policy
- [ ] Publish sanitized cross-language fixtures
- [ ] Define non-reusable proxy-access enforcement

### QA/Release

- [ ] Run clean-package cross-repository negative matrix
- [ ] Verify exactly-once valid start and zero-side-effect failures
- [ ] Verify bounded cleanup/no orphan
- [ ] Run secret sentinel scan across runtime and package artifacts
- [ ] Record Security and QA approval before release

---

**ข้อสรุป:** production adapters ยังเป็นงานที่ต้อง implement และ integrate จริงทั้งฝั่ง Launcher, Backend และ NekoProxyCore เอกสารนี้ไม่ใช่ release approval; production wiring ต้องคง fail closed จนกว่า contract acceptance, adapter tests, real E2E และ Security/QA gates จะผ่านครบ
