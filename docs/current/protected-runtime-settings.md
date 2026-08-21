# Protected runtime settings provisioning

## Approved design

Production Core consumes `runtime-settings.nkps`, an AES-256-GCM authenticated
payload created from an external trusted Netch `settings.json`. The sealing key
is a separate 32-byte build/provisioning input embedded in the Core host assembly
only for the approved production publish. Neither plaintext settings nor the key
is source-controlled. Core decrypts and parses bytes directly in memory through
the existing Netch `Setting` and `ServerConverterWithTypeDiscriminator`
semantics, validates one production profile with at least one server and a unique opaque PSO2 candidate, and never writes decrypted settings to disk.

Rejected alternatives:

- ordinary plaintext `data/settings.json`: disclosure and accidental shipping;
- plaintext embedded resource: extraction is casual and unauthenticated;
- remote provisioning: unnecessary Backend/Launcher contract expansion for the
  stated threat model;
- plaintext fallback/default settings: violates fail-closed production behavior.

A locally provisioned protected payload is supported by the same external-input
sealing command when a release is assembled on the target environment.

## Security properties and limitation

- `CASUAL_PLAINTEXT_DISCLOSURE_PROTECTION = YES`
- `TAMPER_DETECTION = YES`
- `FAIL_CLOSED = YES`
- `DETERMINED_LOCAL_REVERSE_ENGINEERING_PROTECTION = NOT GUARANTEED`
- `CLIENT_SIDE_KEY_RECOVERY_BY_DETERMINED_REVERSE_ENGINEERING_IS POSSIBLE`

This boundary prevents ordinary plaintext packaging, casual inspection, and
accidental disclosure. It does not claim extraction is impossible for a
determined reverse engineer with local access to the client process or binary.

## Provision and rotate

Keep the three sensitive paths outside the repository and pass the trusted
packaged mode root explicitly:

```text
dotnet run --project NekoProxyCore.SettingsTool/NekoProxyCore.SettingsTool.csproj \
  -c Release -- seal <external-settings.json> <runtime-settings.nkps> <runtime-settings.key> \
  <trusted-mode-root>
```

Successful output contains structural facts only: exactly one profile, at least
one server, PSO2 profile present, and valid canonical `profile-0/server-0`
relationship. Acceptance also requires exactly one packaged PSO2 mode match.
The historical exact five-server requirement is stale and superseded by this
AT_LEAST_ONE policy. It never emits server values or credentials. The command
uses a fresh random AES key and nonce, refuses existing output paths, cleans
partial output on failure, and exits nonzero on invalid input.

Freeze the one approved payload/key pair before reproducibility builds. Publish
Build A and Build B with the exact same external files:

```text
dotnet publish NekoProxyCore.Host/NekoProxyCore.Host.csproj \
  -c Release -f net6.0-windows -r win-x64 -p:Platform=x64 \
  --self-contained false \
  -p:NekoProtectedSettingsPayload=<runtime-settings.nkps> \
  -p:NekoProtectedSettingsKeyFile=<runtime-settings.key> \
  -o <publish-directory>
```

The publish target authenticates/decrypts the exact payload/key pair and invokes
the same production acceptance validator against the packaged `Storage/mode`
bundle that startup uses. It requires the unique valid opaque candidate to be
exactly `profile-0/server-0` with one PSO2 mode match. A mismatched, tampered,
malformed, or runtime-incompatible pair blocks publish instead of producing an
artifact that fails later.

The authorized SOCKS child path feeds its generated client configuration to
`v2ray-sn.exe` over redirected standard input (`run -c stdin:`), then clears the
managed byte buffer. It does not create the legacy plaintext `data/last.json`
transient file.

Release only the protected payload and binaries. Production publish requires fresh
`Redirector/bin/Release/Redirector.bin` and `Redirector/bin/Release/nfapi.dll`,
then stages both as `bin/Redirector.bin` and `bin/nfapi.dll`; missing native
inputs fail publish. Never include the standalone key file or external plaintext
input. For rotation, repeat the external sealing
step with the rotated settings, freeze and hash the new protected payload, run
tests/reproducibility/smoke, and release the new artifact. The old plaintext and
key remain outside source control and release output.

## Artifact rules

The manifest may include only artifact identity (`path`, `size`, `sha256`, and
non-secret format/version metadata). It must not contain plaintext settings,
server fields, credentials, key bytes, or decrypted values.
