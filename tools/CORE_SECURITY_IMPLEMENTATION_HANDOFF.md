# NekoProxyCore — Core Security Implementation Handoff

วันที่จัดทำ: 2026-08-03  
Repository: `D:\NekoProxyCore`  
Branch ที่ตรวจ: `feature/neko-headless`  
ผู้รับผิดชอบปลายทาง: NekoProxyCore, Security, Backend integration และ QA teams  
สถานะ: **ACTION REQUIRED — CORE AUTHORIZATION PARTIAL, PRODUCTION HOST BLOCKED**

เอกสารที่ต้องอ่านร่วมกัน:

- `tools/STEP_E_SECURITY_AUTHORIZATION_REPORT.md`
- `tools/LAUNCHER_CORE_AUTHORIZATION_ADAPTER_HANDOFF.md`
- `tools/PROCESSMODE_TEST_REPORT.md`

> เอกสารนี้เป็น sanitized source of truth สำหรับทีมที่รับช่วง implementation ความปลอดภัยฝั่ง Core ไม่มี production endpoint, access token, proxy credential, private key, customer identifier หรือ raw runtime configuration

---

## 1. Executive handoff

ProcessMode/runtime integration ของ Step D ผ่านกับ `pso2.exe` และ gameplay จริงแล้ว แต่ production authorization ของ Step E ยังไม่พร้อม

Core checkpoint ปัจจุบันมีสิ่งต่อไปนี้:

- มี `IProxyStartAuthorizer` ที่ `NekoProxyCore.Core/AuthorizationContracts.cs`
- `HeadlessRuntimeCoordinator.StartAsync` เรียก authorizer ก่อน mode controller/engine
- default constructor ใช้ `AuthorizationRequiredStartAuthorizer` และ fail closed
- missing authorization คืน typed `AuthorizationRequired`
- focused test ยืนยัน authorization failure ทำให้ engine start count เป็นศูนย์
- เริ่มมี typed authorization error taxonomy

Core checkpoint ปัจจุบันยังขาด:

- Core-generated cryptographic challenge
- expiry และ atomic one-attempt challenge consumption
- canonical configuration hash implementation/fixtures
- strict RS256 JWT verifier
- pinned public-key ring และ rotation policy implementation
- protocol v2 `challenge` และ mandatory-permit `start`
- production current-user named-pipe host
- single-instance lease และ production executable artifact
- complete authorization failure test matrix
- cross-language fixtures กับ Backend/Launcher
- continuous authorization/renewal enforcement
- short-lived proxy access material integration

**Decision:** ทีมถัดไปห้ามประกาศว่า security พร้อม ห้ามเปิด production host และห้ามเพิ่ม offline/debug/allow-all fallback จนกว่า acceptance gates ในเอกสารนี้จะผ่าน

---

## 2. Security property ที่ Core ต้องรับประกัน

Core ต้องรับประกันตามขอบเขตที่เป็นไปได้ว่า:

1. การเรียก executable, named pipe หรือ public runtime API เพียงอย่างเดียวไม่สามารถเริ่ม ProcessMode ได้
2. ทุก start attempt ต้องมี permit ใหม่ที่ Backend เซ็นหลัง online authorization
3. permit ต้องผูกกับ Core process/start attempt ผ่าน one-use challenge
4. permit ต้องผูกกับ exact security-relevant configuration ผ่าน SHA-256
5. signature, claims, time, challenge และ configuration ต้องผ่านก่อน runtime side effects
6. `pso2.exe` ต้องถูกตรวจซ้ำหลัง authorization และก่อน engine start
7. challenge/permit ห้าม reuse แม้ start attempt แรกล้มเหลว
8. concurrent callers ไม่สามารถ consume authorization เดียวกันสำเร็จมากกว่าหนึ่งครั้ง
9. authorization failure ทุกกรณีต้องมี engine start count เท่ากับศูนย์
10. token/claims/identifiers/secrets ต้องไม่ออกทาง wire response, log, file, telemetry หรือ crash detail

Core ไม่ต้องและไม่สามารถอ้างว่าต้าน local administrator ที่ patch executable/public key หรือ inject memory ได้ 100% เป้าหมายคือให้ Backend เป็น authority, normal bypass/replay fail closed และไม่แจก reusable secret โดยไม่จำเป็น

---

## 3. Trust boundary และสิ่งที่ Core ห้ามเชื่อ

Core เชื่อได้เฉพาะ permit ที่:

- signature ผ่านด้วย pinned public key ของ exact `kid`
- `alg` เป็น exact `RS256`
- claims และเวลาตรง frozen policy
- challenge ตรงกับ outstanding Core challenge
- configuration hash ตรงกับ start request

Core ห้ามใช้สิ่งต่อไปนี้เป็น authorization proof:

- Launcher PID, parent PID, executable path หรือ code signature เพียงอย่างเดียว
- Windows user identity หรือ `CurrentUserOnly` pipe
- mutex/single-instance ownership
- process name หรือการพบ `pso2.exe`
- raw `sessionId`, installation hash หรือ account ID
- `authorized=true` หรือ unsigned JSON
- shared secret ที่ฝังใน client
- local entitlement state ของ Launcher
- permit ที่ Launcher สร้างหรือ sign เอง

`CurrentUserOnly`, process ownership, executable integrity และ target verification เป็น defense-in-depth/transport controls ไม่ใช่ server authorization

---

## 4. สถานะ source ปัจจุบันที่ทีมรับช่วงต้องรู้

### 4.1 Core authorization seam

ไฟล์:

- `NekoProxyCore.Core/AuthorizationContracts.cs`
- `NekoProxyCore.Core/HeadlessRuntimeCoordinator.cs`
- `NekoProxyCore.Core/ProxyErrorCode.cs`
- `Tests/HeadlessRuntimeTests.cs`

พฤติกรรม checkpoint:

- constructor ที่ไม่รับ authorizer inject `AuthorizationRequiredStartAuthorizer`
- constructor สำหรับ composition รับ `IProxyStartAuthorizer`
- `StartAsync` เรียก authorizerก่อน publish `Starting` และก่อน `_modeController.StartAsync`
- error จาก authorizerถูก map เป็น typed failed result

ข้อจำกัด:

- `IProxyStartAuthorizer.AuthorizeAsync` ยังรับเพียง `ProxyStartRequest`
- `ProxyStartRequest` ยังไม่มี permit/challenge authorization envelope
- default deny object ยังไม่ใช่ production verifier
- exception mapping จาก authorizerยังต้อง harden ให้ไม่เปิด raw detail
- ยังไม่มี proof ว่า alternate future host entry pointทุกทางใช้ seam นี้

### 4.2 Host protocol checkpoint

ไฟล์:

- `NekoProxyCore.Host/Protocol/ControlProtocol.cs`
- `NekoProxyCore.Host/Protocol/ControlRequest.cs`
- `NekoProxyCore.Host/Protocol/ControlResponse.cs`

พฤติกรรมปัจจุบัน:

- protocol version เท่ากับ `1`
- commands มี `Start`, `Status`, `Stop`
- frame maximum เท่ากับ 8 KiB
- `start` ตรวจ `pso2.exe` และ opaque `profile-N`/`server-N`
- `ControlRequest.TryCreateStartRequest` สร้าง runtime requestโดยไม่มี permit

ข้อจำกัดสำคัญ:

- protocol v1 ไม่ใช่ production authorization contract
- ไม่มี `challenge` command
- ไม่มี mandatory permit
- ยังไม่มี runnable named-pipe host ใน `NekoProxyCore.Host`
- ห้ามนำ v1 start handlerไปเปิดเป็น production pipe server

### 4.3 Worktree state

ณ เวลาจัดทำเอกสาร งาน authorization checkpoint และเอกสาร handoff ยังเป็น uncommitted changes ทีมรับช่วงต้องรัน `git status`, ตรวจ diff และถือการเปลี่ยนแปลงเดิมเป็น user-owned work ห้าม reset/stash/overwrite โดยไม่รับคำสั่ง

---

## 5. Contract ที่ต้อง freeze ก่อน implementation

Core team ต้องร่วมกับ Launcher, Backend และ Security freeze revision เดียวกันสำหรับ:

- protocol v2 exact JSON schemas
- unknown/duplicate JSON property policy
- frame/token/request size limits
- challenge encoding, length, lifetime และ consume semantics
- canonical configuration text/encoding/hash
- JWT header/claims/types/time semantics
- issuer, audience, product, scope และ key IDs
- public-key ring/rotation policy
- pipe, mutex และ executable identities
- timeouts สำหรับ connect/challenge/start/status/stop
- Backend permit issuance envelope
- renewal/revocation SLA
- proxy access material contract

ห้าม hard-code issuer/audience/key IDs หรือ production public key จากค่าตัวอย่างในเอกสาร ทีม Core ต้องรับค่าที่ Security อนุมัติผ่าน immutable production configuration/build artifact ที่ไม่มี private secret

---

## 6. Recommended Core component boundaries

ชื่อจริงปรับตาม repository convention ได้ แต่ responsibility ต้องไม่ปะปนกัน:

```text
ICoreChallengeService
  Issue() -> CoreChallenge
  ConsumeForAttempt(challenge, now) -> ChallengeConsumption

IMonotonicClock
  GetTimestamp()
  GetElapsedTime(start, end)

ICanonicalStartConfiguration
  Serialize(configuration) -> UTF-8 bytes
  ComputeSha256(configuration) -> fixed representation

IPermitKeyResolver
  ResolveExact(kid) -> verification key or unknown-key result

ILaunchPermitVerifier
  VerifyAndAuthorize(permit, challenge, configuration, wallClockNow)
    -> typed AuthorizationDecision

IProxyStartAuthorizer
  AuthorizeAsync(authorizedStartAttempt)
    -> typed sanitized decision

IControlProtocolV2
  ParseBoundedFrame
  SerializeAllowListedResponse

ICoreHost
  current-user pipe accept loop
  single-instance lease
  deterministic shutdown
```

กฎการออกแบบ:

- crypto/token parsing ห้ามอยู่ใน pipe handlerโดยตรง
- challenge state ห้ามอยู่ใน Launcher-facing DTO อย่างเดียว
- Core layerไม่ควรอ้าง WinForms, pipe implementation หรือ Backend SDK
- Host layer compose verifier/challenge/runtime แต่ห้ามมี authorization bypass
- production key resolverมีเฉพาะ public verification material
- tests ใช้ deterministic clock/test keys ผ่าน explicit test composition เท่านั้น
- ห้ามมี production `AllowAllAuthorizer`

---

## 7. Core-generated challenge requirements

### 7.1 Generation

- ใช้ `RandomNumberGenerator` ของ .NET
- entropy อย่างน้อย 256 bits
- encode base64url ไม่มี padding
- exact encoded sizeถูก validate
- challenge ต้อง unique ต่อ issuance ตาม practical collision resistance
- ห้าม derive จาก PID, timestamp, GUID v1, correlation ID หรือ Launcher input

### 7.2 Storage

- memory only
- ผูกกับ Core process instance
- เก็บ monotonic issued/deadline timestamp
- maximum outstanding challenge policyต้อง freeze; แนะนำ one outstanding challenge ต่อ host/start state
- challenge ใหม่ต้อง invalidate/consume stateเดิมตาม frozen concurrency policy
- ห้าม persist เพื่อทำ replay cacheข้าม process

### 7.3 Lifetime

- อายุไม่เกิน 30 วินาที
- expiry ใช้ monotonic clock เพื่อไม่ให้ wall-clock rollbackยืดอายุ
- wall clockใช้สำหรับ JWT time claims แยกจาก challenge deadline

### 7.4 Consumption

- consume แบบ atomic ใน authorization attempt แรก
- consume ไม่ว่าผล signature/claims/configจะผ่านหรือไม่
- attempt ซ้ำคืน `AuthorizationReplay` หรือ typed policy-equivalent
- expired challengeคืน typed authorization errorโดยไม่บอก validation detailเกินจำเป็น
- concurrent double startต้องมีผู้ชนะได้มากสุดหนึ่ง attempt และ engine startได้มากสุดหนึ่งครั้ง

### 7.5 Required tests

- entropy/decoded lengthถูกต้อง
- base64url schemaถูกต้อง
- issueสองครั้งได้ค่าต่างกัน
- expired challenge fail closed
- consumed challenge reuseไม่ได้
- failed permit consumes attempt
- concurrent consumptionมี successful consumerไม่เกินหนึ่ง
- process/service restartไม่ทำให้ old challenge valid

---

## 8. Canonical configuration binding

Canonical draft ที่ต้องยืนยันร่วมกัน:

```text
protocolVersion=2\n
processName=pso2.exe\n
profileReference=profile-0\n
serverReference=server-0\n
```

Core implementation requirements:

- UTF-8 ไม่มี BOM
- LF ทุกบรรทัดรวมบรรทัดสุดท้าย
- fixed key order
- exact lowercase `pso2.exe`
- opaque referencesต้อง canonical ก่อน hashing
- SHA-256 ผ่าน .NET cryptographic API
- compare digestแบบ constant-time
- reject unsupported protocol version/configurationก่อน engine start
- shared fixtureต้องมี canonical bytes/text และ expected hash

ห้าม hash arbitrary client JSON เพราะ property order, whitespace และ encodingไม่ stable ห้าม include credential ใน canonical configuration

Required tests:

- expected fixture hashตรงกัน
- line ending/casing/order deviationไม่ match
- profile/server changeทำ permit invalid
- process name canonicalizationไม่เปิดให้ targetอื่น
- oversized/invalid referencesไม่ถึง verifier/engine

---

## 9. Strict RS256 launch permit verifier

### 9.1 Header validation

ต้อง require:

- compact JWT มีสาม segments
- bounded total/segment sizesก่อน decode
- valid base64url
- headerเป็น JSON object
- `typ` exact policy value
- `alg` exact `RS256`
- `kid` present, bounded และ exact known key
- reject `alg=none`, HS/RS confusion และ unknown/missing `kid`
- reject duplicate security-relevant properties

ห้ามเลือก algorithmจาก tokenแล้วปล่อย library auto-negotiate ห้าม fallback unknown `kid` ไป keyแรกหรือทุก key

### 9.2 Signature validation

- ใช้ approved .NET cryptographic/JWT implementationที่รองรับ explicit RS256 policy
- public keyเท่านั้นใน client
- verify exact signing input bytes
- signature failure map เป็น `AuthorizationInvalid`
- exception detailห้ามออก wire/log

หากต้องเพิ่ม NuGet dependency ต้องผ่าน dependency/security review, pin version และตรวจ neighboring project conventionsก่อนแก้ manifest

### 9.3 Required claims

| Claim | Core validation |
|---|---|
| `iss` | exact trusted issuer |
| `aud` | exact Core launch audience |
| `sub` | valid expected UUID shape |
| `sid` | valid launcher session UUID |
| `iid` | valid installation UUID |
| `lid` | valid license UUID |
| `product` | exact approved product |
| `scope` | exact `proxy:start` |
| `cfg` | exact canonical configuration digest |
| `challenge` | exact outstanding Core challenge |
| `jti` | bounded random identifier, minimum entropy representation policy |
| `iat` | required numeric date |
| `nbf` | required numeric date |
| `exp` | required numeric date |

Coreไม่ควรส่ง identity claimsกลับ response/log/telemetry Claimsมีไว้ validateและ runtime bindingภายในเท่านั้น

### 9.4 Time policy

- permit recommended TTL 30 วินาที
- hard reject `exp - iat > 60` วินาที
- allowed skewไม่เกิน frozen policyประมาณ 5 วินาที
- reject expired/not-yet-valid/future-issued/excess-lifetime permit
- use injectable UTC wall clockสำหรับ deterministic tests
- challenge monotonic expiryต้องผ่านแยกต่างหาก

### 9.5 Parse hardening

- reject malformed JSON/UTF-8/base64url
- reject missing/duplicate claims
- reject wrong claim types, arrays/objectsแทน scalar
- reject malformed UUIDs/timestamps
- reject oversized token/header/payload/signature
- reject unsupported critical headers
- catch crypto/parser exceptionsและคืน typed sanitized failure

---

## 10. Authorization order and atomicity

Production start flow ภายใน Core ต้องเป็น:

```text
bounded frame parse
→ bounded JWT structural parse
→ atomically claim/consume challenge attempt
→ resolve exact kid
→ require RS256 and verify signature
→ validate issuer/audience/product/scope/claims/time/lifetime
→ constant-time challenge comparison
→ recompute and constant-time compare cfg
→ validate immutable runtime request
→ final pso2.exe recheck
→ publish Starting
→ controller/engine/driver/network start
→ publish Running
```

Security ต้อง freezeว่าการ consumeเกิดก่อนหรือหลัง structural parseระดับใด แต่เมื่อ requestถูกยอมรับเป็น authorization attemptแล้ว failureทุกชนิดต้อง consume challenge ป้องกัน oracle/retry probing

ข้อบังคับ:

- lifecycle gateและ challenge consumptionต้องไม่เปิด TOCTOU concurrent start
- authorizationผ่านแต่ targetหายต้องไม่คืน permit/challengeให้ reuse
- publish `Starting` หลัง authorizationผ่านเท่านั้น
- configuration objectที่ hashแล้วต้องเป็น objectเดียวกับที่ส่งให้ controller ห้าม mutable re-resolutionกลางทาง
- alternate entry points เช่น integration adapter/host handlerต้องผ่าน shared innermost seam

---

## 11. Protocol v2 requirements

Protocol v2 ต้องเพิ่ม `Challenge` command และ mandatory permitใน `Start`

### 11.1 Challenge request draft

```json
{
  "version": 2,
  "command": "challenge",
  "correlationId": "launcher-001"
}
```

Sanitized response draft:

```json
{
  "version": 2,
  "kind": "challenge",
  "correlationId": "launcher-001",
  "challenge": "<core-generated-base64url>",
  "expiresInSeconds": 30
}
```

ห้ามส่ง wall-clock expiryหากไม่จำเป็น Launcherไม่ใช่ authorityของ challenge validity

### 11.2 Start request draft

```json
{
  "version": 2,
  "command": "start",
  "correlationId": "launcher-002",
  "processName": "pso2.exe",
  "profileReference": "profile-0",
  "serverReference": "server-0",
  "permit": "<compact-signed-jwt>"
}
```

### 11.3 Parser rules

- bounded UTF-8 frame
- object rootเท่านั้น
- exact version/command allow-list
- required fieldsครบและ typeตรง
- reject duplicate fields
- freeze unknown-field policy; security recommendationคือ rejectใน security-sensitive requests
- correlation ID opaque, bounded, non-secret
- permit mandatory/non-empty/boundedเฉพาะ start
- status/stopไม่มี permitเว้นแต่ continuous-auth policyกำหนดภายหลัง
- v1 `start` ต้อง rejectใน production host

### 11.4 Response rules

Response allow-listเท่านั้น:

- protocol version
- kind
- correlation ID
- typed status
- succeeded
- error code
- challenge fieldsเฉพาะ challenge response

ห้ามมี:

- exception message/stack
- token/header/claims
- user/session/license/installation IDs
- key material
- proxy endpoint/credential
- raw configuration

---

## 12. Production headless host requirements

Core teamต้องสร้าง runnable artifactจริง ไม่ใช่ protocol libraryอย่างเดียว:

- production executableเป็น `NekoProxyCore.exe` หรือชื่อที่ contract freeze
- `WinExe`, x64
- no console, form, tray, notification หรือ message box
- current-user-only named pipe
- single-instance lease/mutex
- bounded accept/read/write/stop timeouts
- deterministic cancellation and shutdown
- pipe disconnectไม่ทิ้ง partial start/running state
- owned helper/runtime cleanup
- no permit/token in process argv/environment
- no dynamic runtime rootจาก untrusted argv
- code-signing/artifact integrityเป็น downstream release gate

Host composition rootต้อง inject production challenge service, key ring และ verifierอย่าง explicit หาก dependencyหายต้อง fail startup/authorization closed ห้าม fallbackเป็น default allow

Host readinessต้องมี typed protocol signal ไม่ใช่เพียง processอยู่หรือ pipeชื่อปรากฏ

---

## 13. Public-key custody and rotation

Coreมีได้เฉพาะ public verification keys:

- exact key ID → exact public key mapping
- unknown key reject
- private keyห้ามเข้า repository/build/package/test artifact ยกเว้น dedicated non-production test keyที่ Security อนุมัติ
- rotationปกติ: ship old+new public keys → Backend signด้วย new → รอ max TTL+skew → retire oldใน releaseถัดไป
- emergency remote manifestต้อง signedด้วย offline root; ห้าม trust unsigned JWKS/manifest
- key resolverไม่ fetch arbitrary URLจาก token header
- logได้เฉพาะ sanitized key ID/result codeตาม policy ห้าม log token

ทีมรับช่วงต้องขอ approved test public/private key fixturesจาก Security/Backend; production private keyต้องไม่ถูกส่งให้ Core team

---

## 14. Typed errors and sanitization

Core taxonomyขั้นต่ำ:

- `AuthorizationRequired`
- `AuthorizationInvalid`
- `AuthorizationExpired`
- `AuthorizationReplay`
- `AuthorizationUnavailable`
- `SessionInactive`
- `ProcessNotFound`
- `ProcessExited`
- lifecycle errorsเดิม

ข้อกำหนด:

- external responseไม่แยก signatureผิด, issuerผิด, claimผิดละเอียดเกิน policy
- internal aggregate metricsอาจแยก sanitized categoryโดยไม่มี token/identity
- parser/crypto exceptionต้องไม่ใช้ `exception.Message` ตรงใน wire result
- authorization failureไม่ควรเผย token fragment, decoded header/claim หรือ expected value
- status sink/loggingต้องใช้ error codeและ sanitized static message

ต้อง review `HeadlessRuntimeCoordinator` generic exception pathเพื่อรับรองว่า authorizer/crypto exceptionไม่ทำ secretหลุดผ่าน `e.Message`

---

## 15. Continuous authorization and proxy access

Launch permitป้องกันเฉพาะ start event ทีม Coreต้องเตรียม seamแต่ห้าม invent final protocolก่อน Security freeze:

- runtime authorization binding
- signed renewal/challenge-response
- bounded renewal interval/grace period
- fail-closed stopเมื่อ renewalหมดอายุ
- target-exit stopยังคงบังคับ
- Launcher signalเป็น orchestration signal ไม่ใช่ authorityเดียว

Proxy accessเป็น boundaryแยก:

- Coreห้าม resolve static reusable credentialจาก shipped bundleใน production design
- รองรับ per-session/short-lived access materialผ่าน protected in-memory pathหลัง permit validation
- ห้ามเขียน credentialลง disk/log/status/error
- credential expiry/revocationต้องบังคับได้ server-side

จนกว่า Backend/Proxy Server freeze architecture ให้ classifyว่า `BLOCKED/UNVERIFIED`

---

## 16. TDD implementation order

ทุก behavior changeต้องใช้ RED → GREEN → REFACTOR และเห็น test failด้วยเหตุผลที่คาดก่อนแก้ production code

แนะนำ vertical slices:

### C1 — Challenge primitive

- RED: challenge decoded entropyอย่างน้อย 32 bytes
- GREEN: cryptographic generator
- RED: expired challenge reject
- GREEN: monotonic deadline
- RED: failed first attemptทำ replayไม่ได้
- GREEN: atomic consume

### C2 — Canonical configuration

- RED: fixture expected hash
- GREEN: deterministic serializer/hash
- RED: profile/server/process deviation mismatch
- GREEN: exact binding

### C3 — JWT structural/header validation

- RED: missing/malformed/oversized/duplicate/`alg=none`/unknown `kid`
- GREEN: bounded strict parserและ key resolver

### C4 — Signature/claims/time

- RED: invalid signature/issuer/audience/scope/product/time/lifetime
- GREEN: strict RS256 verifier
- RED: wrong challenge/config
- GREEN: constant-time binding checks

### C5 — Innermost seam integration

- RED: every auth failure engine count 0
- GREEN: verifier wiring
- RED: alternate entry point bypass
- GREEN: shared production seam

### C6 — Protocol v2

- RED: v1/missing permit/unknown fields/oversized frames reject
- GREEN: challenge/start parser and response serializer

### C7 — Production host

- RED: same-user pipe/single instance/readiness/cleanup behaviors
- GREEN: headless host
- RED: crash/timeout/disconnect orphan checks
- GREEN: deterministic lifecycle cleanup

ห้ามเขียน testsทั้งหมดก่อน implementationทั้งหมด ให้ทำ vertical sliceทีละ behavior

---

## 17. Required Core test matrix

### Challenge

- valid generation/encoding/size
- expiry boundary
- consume-on-success
- consume-on-failure
- replay
- concurrent double consume
- cancellation/process restart semantics

### JWT structure and crypto

- missing/malformed/oversized token
- invalid base64url/JSON
- wrong segment count
- `alg=none`, HS algorithm, unsupported algorithm
- missing/unknown/oversized `kid`
- invalid signature
- duplicate header/claims
- unsupported critical header

### Claims and time

- wrong/missing issuer/audience/scope/product
- malformed `sub`/`sid`/`iid`/`lid`
- missing/malformed `jti`
- expired/not-yet-valid/future-issued
- excess lifetime
- skew boundaries
- wrong/reused/expired challenge
- wrong configuration hash

### Runtime boundary

- missing permit → engine 0
- every invalid permit case → engine 0
- valid permit + no target → engine 0
- valid permit + target exits during startup → typed failure/cleanup
- valid permit + target → exactly one engine start
- concurrent double start → at most one engine start
- alternate host/adapter entry path cannot bypass authorizer
- authorization failure publishes no `Starting`

### Protocol/host

- frame boundaries and partial/malformed frames
- v1 start rejected
- mandatory permit
- unknown/duplicate fields
- correlation matching
- allow-listed response only
- current-user pipe configuration
- single instance
- readiness timeout
- disconnect/cancel/crash cleanup
- no orphan process/helper/pipe/mutex/temp state

### Leakage

ใช้ sentinelเท่านั้นและตรวจว่าไม่พบใน:

- response
- status sink/log
- argv/environment
- temp/cache/config files
- telemetry fixture
- exception/crash artifactที่ testสร้าง

---

## 18. Build, artifact and verification gates

ก่อนส่งต่อทุก checkpoint ให้รัน documented .NET toolchainและเก็บผลจริง:

```text
dotnet restore Tests/Tests.csproj
dotnet build NekoProxyCore.Core/NekoProxyCore.Core.csproj -c Release --no-restore
dotnet build NekoProxyCore.Host/NekoProxyCore.Host.csproj -c Release --no-restore
dotnet test Tests/Tests.csproj -c Release --no-restore
git diff --check
```

เมื่อมี production host:

- build/publish `win-x64`
- verify executable subsystem/headless behavior
- collect artifact path, size, SHA-256 และ dependency manifest
- start without target/permitและยืนยัน no runtime side effects
- test pipe readiness/status/stop
- confirm cleanup/no orphan

Legacy Windows buildและ real ProcessMode gateยังต้องใช้ documented toolchainจาก handoffเดิม ห้ามถือ managed unit testsว่าแทน live integrationได้

Warningเดิมต้องรายงานตามจริง ห้าม suppressเพื่อทำผลให้ดูสะอาด

---

## 19. Core merge gates

### Authorization primitive gate

- [ ] challenge cryptographic, bounded, monotonic-expiring และ one-attempt
- [ ] canonical configuration fixture/hashผ่าน
- [ ] strict RS256/header/key/claims/time verifierผ่าน
- [ ] production private keyไม่มีใน repository/artifact

### Runtime gate

- [ ] default path fail closed
- [ ] every authorization failure engine start count 0
- [ ] authorizationก่อน `Starting`/controller/engine/driver/network
- [ ] final exact `pso2.exe` recheck
- [ ] concurrent/alternate entry bypass testsผ่าน

### Protocol/host gate

- [ ] protocol v2 challenge/start/status/stop freezeและ implement
- [ ] v1 start rejectใน production
- [ ] bounded current-user pipe
- [ ] single-instance lease
- [ ] typed sanitized responses
- [ ] headless x64 production artifact
- [ ] bounded deterministic cleanup

### Integration gate

- [ ] shared fixture revisionเดียวกับ Backend/Launcher
- [ ] Backend-generated permit verifyได้จริง
- [ ] Launcher production adapterได้ typed `Running`
- [ ] authorized/unauthorized E2Eผ่าน
- [ ] target exit/revocation policy cleanupผ่าน
- [ ] package/direct proxy access gateผ่าน
- [ ] Security residual-risk sign-off

---

## 20. Files expected to change or be added

ทีมถัดไปต้องตรวจ repositoryจริงก่อนเลือกชื่อไฟล์ ห้ามสร้างตามรายการนี้โดยไม่ trace symbols/usages รายการนี้เป็น responsibility mapไม่ใช่คำสั่งให้สร้างทุกไฟล์ทันที

Likely Core areas:

```text
NekoProxyCore.Core/
  AuthorizationContracts.cs
  HeadlessRuntimeCoordinator.cs
  ProxyStartRequest.cs or a dedicated authorized-attempt model
  ProxyErrorCode.cs
  challenge implementation
  canonical configuration implementation
  permit verifier and public-key resolver

NekoProxyCore.Host/Protocol/
  ControlProtocol.cs
  ControlRequest.cs
  ControlResponse.cs
  protocol v2 DTO/parser additions

NekoProxyCore.Host/
  production host composition and current-user pipe implementation

Tests/
  challenge tests
  canonical configuration fixtures/tests
  JWT positive/negative verifier tests
  protocol v2 tests
  runtime bypass/engine-count tests
  host lifecycle/leakage tests

security-contract/ or approved shared-fixture location
  schemas, canonical text/hash, public test vectors
```

หลักการ dependency:

- Core security primitivesไม่ควร depend on Host/Windows/Legacy
- Host depend on Coreและ compose Windows transport/runtime adapters
- Testsอาจ reference test fixturesแต่ production assembliesห้าม embed private test key

---

## 21. Handoff package ที่ทีม Core ต้องส่งให้ทีมถัดไป

ทุก security checkpointต้องส่ง primary evidenceแบบ sanitized:

1. revision/branch/worktree status
2. changed-file listและ responsibility summary
3. frozen contract revision/hash
4. test commandsและ exact passed/failed/skipped counts
5. build/publish commandsและ result
6. artifact path/size/SHA-256/dependency manifestเมื่อมี host
7. authorization negative-matrix resultพร้อม engine start count
8. protocol fixturesและ signature-vector result
9. leakage sentinel scan result
10. known blockers/TBD/approvals
11. residual risks
12. confirmationว่าไม่มี token/private key/proxy credential/customer dataใน handoff

ห้ามส่ง:

- raw token/JWT from approved environment
- production key/JWK private parameters
- real settings/profile/server URI
- customer/account/session/license/installation identifiers
- packet payloadหรือ unsanitized logs

---

## 22. Immediate next actions in dependency order

ทีม Core ที่รับช่วงควรทำตามลำดับนี้:

1. ตรวจและ preserve uncommitted checkpoint ปัจจุบัน
2. นัด contract freeze S0 กับ Launcher/Backend/Security
3. สร้าง shared canonical config fixtureและ dedicated test-key signature vectors
4. ใช้ TDD implement challenge generation/expiry/atomic consume
5. ใช้ TDD implement canonical serializer/hash
6. ใช้ TDD implement bounded strict RS256 verifier/key ring
7. harden authorizer exception/result sanitization
8. integrate authorized attemptที่ innermost runtime seam
9. prove full authorization failure matrix engine count 0
10. implement protocol v2 challenge + mandatory permit start
11. implement production headless current-user pipe hostและ single instance
12. publish artifact manifestให้ Launcher
13. run cross-repository fixturesและ Backend-generated permit test
14. run authorized/unauthorized real integration gate
15. add continuous authorizationและ proxy access enforcementตาม approved policy
16. obtain Security/QA sign-off

งานข้อ 4–11 ห้ามถูกแทนด้วยเอกสารหรือ stub ต้องมี executable tests/build/artifact evidenceจริง

---

## 23. Stop conditions and escalation

หยุด implementationและขอ decisionหาก:

- Securityยังไม่ freeze claims/time/key rotation
- Backend/Launcher schemaไม่ตรงกัน
- ต้องเลือกระหว่าง incompatible JWT/parser libraries
- proposed retry semanticsอาจ reuse challenge/permit
- production hostต้องรับ secretผ่าน argv/env/file
- static reusable proxy credentialยังเป็น requirement
- continuous authorization policyกระทบ runtime lifecycleแต่ยังไม่มี SLA
- testต้องใช้ production private keyหรือ real credential
- alternate entry pointไม่สามารถวางใต้ shared authorizer seam

ห้ามแก้ด้วย compatibility fallbackที่ลด security

---

## 24. Current readiness classification

| Boundary | State |
|---|---|
| Step D ProcessMode/gameplay | PASS ตามรายงานเดิม |
| Core innermost authorization seam | PARTIAL — checkpoint มีแล้ว |
| Default missing-authorization behavior | PARTIAL/VERIFIED — fail closed focused test |
| Challenge lifecycle | NOT IMPLEMENTED |
| Canonical config binding | NOT IMPLEMENTED |
| RS256 permit verification/key ring | NOT IMPLEMENTED |
| Protocol v2 mandatory permit | NOT IMPLEMENTED |
| Production headless pipe host | NOT IMPLEMENTED |
| Backend permit interoperability | NOT EVIDENCED |
| Continuous authorization | POLICY/IMPLEMENTATION BLOCKED |
| Direct proxy credential protection | UNVERIFIED/BLOCKED |
| Core security overall | BLOCKED/PARTIAL |
| Production release | NOT GRANTED |

คำว่า **Core security ready** ใช้ได้เมื่อ authorization primitive, runtime, protocol/host และ cross-repository integration gatesผ่านครบพร้อม Security sign-offเท่านั้น
