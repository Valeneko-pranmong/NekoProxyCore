# NekoProxyCore

Headless Windows proxy Core สำหรับ Neko Launcher โดยสืบต่อ network engine จาก Netch 1.x
และแยก product host, runtime contracts, Windows integration และ legacy ProcessMode adapter
ออกจาก WinForms UI เดิม

## จุดเริ่มต้น

- **Branch งานหลัก:** `feature/neko-headless`
- **Frozen compatibility baseline:** `origin/baseline/netch-1.9.7`
  (`99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687`)
- **เอกสารสถานะปัจจุบัน:**
  [`docs/current/core-release-handoff.md`](docs/current/core-release-handoff.md)
- **สารบัญเอกสาร:** [`docs/README.md`](docs/README.md)

> `main` เป็น legacy/reference branch ไม่ใช่จุดพัฒนาปัจจุบัน

## สถานะปัจจุบัน

- Canonical `Release`/`win-x64` publish จาก source ผ่านแล้ว
- NEKO-AUTH-LITE/lite-v1 verifier, Named Pipe `NekoProxyCoreControl`, Protocol v2 และ fail-closed lifecycle ผ่านการตรวจ
- Lite source/test preparation complete; hosted cutover, production artifact และ cross-component E2E ยังไม่ทำ
- Production authorization remains fail closed on invalid/missing permit; Core holds public-key authority only

- Runtime bundle ส่งให้ทีม Launcher แยกจาก Git ที่
  `release/NekoProxyCore-win-x64.zip` พร้อม manifest, provenance, verification report และ
  `SHA256SUMS.txt`

สถานะและข้อจำกัดฉบับเต็มอยู่ใน
[`docs/current/core-release-handoff.md`](docs/current/core-release-handoff.md)

## โครงสร้าง source

| Path | บทบาท |
|---|---|
| `NekoProxyCore.Core/` | Runtime contracts, authorization และ headless coordinator |
| `NekoProxyCore.Host/` | Windows headless executable และ Named Pipe host |
| `NekoProxyCore.Windows/` | Windows process integration |
| `NekoProxyCore.Legacy/` | Adapter เข้าสู่ Netch ProcessMode engine |
| `Netch/` | Legacy Netch engine source ที่ยังใช้ระหว่าง migration |
| `Storage/` | Runtime mode/i18n/assets ที่ publish ไปกับ Core |
| `Tests/` | Managed unit, security และ packaging regression tests |
| `docs/current/` | เอกสารสถานะที่ใช้งานปัจจุบันเท่านั้น |
| `docs/reference/` | Reference ที่ยังใช้และไม่ใช่ status report |
| `docs/archive/` | Historical plans/reports; ห้ามใช้แทน current status |
| `release/` | Generated handoff bundles; ignored by Git |

## Build และตรวจสอบ

```bash
dotnet test Tests/Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo

dotnet publish NekoProxyCore.Host/NekoProxyCore.Host.csproj \
  -c Release \
  -f net6.0-windows \
  -r win-x64 \
  -p:Platform=x64 \
  --self-contained true \
  -o TestResults/canonical-release \
  --nologo \
  -m:1
```

Production Core bundle เป็น self-contained win-x64 ทำให้ target users ไม่จำเป็นต้องติดตั้ง .NET 6 Windows Desktop Runtime เพิ่มเติม

## Security rules

ห้ามทำให้ `start` ผ่านด้วย allow-all/no-op verifier, local signer, static/shared secret,
cached permit, first-key fallback หรือ authorization bypass ทุกชนิด ระหว่างที่ production
trust material ยังไม่พร้อม ค่าเริ่มต้นที่ถูกต้องคือ fail closed และ engine start count เป็นศูนย์

## Legacy Netch reference

README ของ upstream Netch เดิมถูกเก็บไว้ที่
[`docs/reference/legacy-netch-upstream.md`](docs/reference/legacy-netch-upstream.md)
เพื่อใช้อ้างอิง compatibility เท่านั้น

## License

โค้ดที่สืบต่อจาก Netch อยู่ภายใต้ GPLv3 ดู [`LICENSE`](LICENSE)
