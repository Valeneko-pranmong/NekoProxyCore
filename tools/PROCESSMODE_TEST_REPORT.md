# ProcessMode Integration Test Report

วันที่ทดสอบ: 2026-08-02  
Branch: `feature/neko-headless`  
Commit: `4241c4b` (`[verified] fix: preserve ProcessMode failure signals`)

## Scope

ทดสอบเฉพาะ ProcessMode ผ่าน official runner โดยใช้ opaque references เท่านั้น:

```powershell
& .\tools\run-processmode-integration.ps1 `
  -ProcessName pso2.exe `
  -ProfileReference profile-0 `
  -ServerReference server-0
```

ไม่มีการบันทึกหรือแสดงค่า profile, server, credential, token, password หรือ runtime settings

## Preparation

- `Original setting\` จัดเตรียมจาก local ProxyCore runtime แล้ว
- ใช้เฉพาะโฟลเดอร์ `data`, `mode`, `bin`, `i18n`
- ตรวจไฟล์ source/destination ครบ 19 ไฟล์ และ SHA-256 ตรงกันทั้งหมด
- `Original setting\` อยู่ใน Git ignore และไม่มี source change จากการจัดเตรียม runtime
- `nfdriver.sys` มีลายเซ็น `Valid` จาก Microsoft Windows Hardware Compatibility Publisher
- service `netfilter2`: `Running`
- official runner preparation: `windowsRuntimeAssets=verified count=3`

## Result

**FAIL — ProcessMode steady state/SOCKS probe ไม่ผ่าน**

Observed sanitized output:

```text
PRECONDITION process=running count=1
PRECONDITION netfilter2=running
CONFIG profiles=1 servers=1 modes=1
EVENT status=Starting error=None
EVENT status=Running error=None
START success=True status=Running error=None
EVENT status=Stopping error=None
EVENT status=Stopped error=None
EVENT status=Failed error=StartFailed
STEADY status=Failed
SOCKS_PROBE success=False
STOP success=True status=Stopped error=None
STOP_AGAIN success=True status=Stopped error=None
CLEANUP controllers=clear
TRAFFIC_GATE result=RequiresTargetVerification
```

The wrapper printed `RUNNER exit=` and returned `INTEGRATION_EXIT=0`; this is not accepted as PASS because steady state was `Failed`, the SOCKS probe failed, and target traffic was not verified.

## Cleanup verification

- Temporary `NekoProcessModeIntegration-*` directories: `0`
- Orphan integration runner/`v2ray-sn.exe` processes: `0`
- `pso2.exe` remained running after the test
- `netfilter2` remained `Running` after the test

## Classification

`FAIL`: runtime entered `Running` transiently but failed before steady state and local SOCKS verification. Further diagnosis of the sanitized `StartFailed` path is required before the integration gate can be marked PASS.
