# ProcessMode runtime configuration discovery contract

This document defines the local, read-only Launcher handoff added in Phase 2.5. It does not change Control Protocol version 2, the S0-RC1 `start` request, the configuration digest, or permit claims.

## Authoritative runtime resolution

`NetchRuntimeBootstrap.InitializeAsync(AppContext.BaseDirectory)` completes before the production pipe server accepts requests. It loads the trusted runtime root's `data/settings.json` through `Configuration.LoadAsync()` and loads mode files into `Global.Modes`. The host then creates one `NetchProcessModeConfigurationCatalog` snapshot and passes the same instance to discovery, validation, and `NetchProcessModeSessionResolver`. The host does not reload configuration during its lifetime.

The shared resolver applies these rules:

1. `profileReference` must match `profile-[0-9]{1,6}` and its numeric suffix is the profile index.
2. Exactly one profile must have that `Profile.Index`; zero or duplicate matches fail closed.
3. `serverReference` must match `server-[0-9]{1,6}` and its numeric suffix is the zero-based position in `Global.Settings.Server`.
4. The server position must exist.
5. `profile.ServerRemark` must equal the selected `server.Remark` using ordinal comparison. Remarks remain internal and are never serialized.
6. Exactly one `Redirector` in the frozen mode set must contain a remark value equal to `profile.ModeRemark` using ordinal comparison. Zero or two-or-more matches fail closed.

Server and `Redirector` objects remain inside `NekoProxyCore.Legacy`.

## Catalog request

Exact request fields:

```json
{"type":"runtimeConfigCatalog","correlationId":"<32 lowercase hex>"}
```

Unknown, missing, duplicate, or incorrectly typed fields are rejected as `ProtocolInvalid` by the existing strict parser.

## Catalog response

Success:

```json
{
  "type": "runtimeConfigCatalogResponse",
  "correlationId": "<same id>",
  "succeeded": true,
  "candidates": [
    {
      "profileReference": "profile-12",
      "serverReference": "server-3",
      "relationshipValid": true,
      "processModeMatchCount": 1
    }
  ]
}
```

Only startable pairs are returned, so every candidate has `relationshipValid=true` and `processModeMatchCount=1`. Candidate fields are exactly `profileReference`, `serverReference`, `relationshipValid`, and `processModeMatchCount`.

Candidates are deduplicated and sorted by numeric profile index ascending, then numeric server index ascending. Ordering provides a stable representation only and never authorizes selecting the first or any available pair.

The limit is exactly 32 candidates. If more than 32 valid pairs exist, the operation fails without truncation and without returning a candidate:

```json
{
  "type": "runtimeConfigCatalogResponse",
  "correlationId": "<same id>",
  "succeeded": false,
  "reason": "CatalogTooLarge"
}
```

The complete catalog failure reason enum is:

- `CatalogUnavailable`
- `CatalogTooLarge`

Zero valid candidates is a successful operation with `candidates=[]`.

Catalog count semantics are:

- 0: no configuration available.
- 1: one unique deterministic candidate exists; Core reports it but does not decide product auto-use policy.
- More than 1: multiple valid candidates exist; Core does not choose among them.

## Validation request

Exact request fields:

```json
{
  "type": "runtimeConfigValidate",
  "correlationId": "<32 lowercase hex>",
  "profileReference": "profile-12",
  "serverReference": "server-3"
}
```

The reference grammars are exactly `profile-[0-9]{1,6}` and `server-[0-9]{1,6}`. Unknown, missing, duplicate, or incorrectly typed fields are rejected as `ProtocolInvalid`.

## Validation response

```json
{
  "type": "runtimeConfigValidateResponse",
  "correlationId": "<same id>",
  "succeeded": true,
  "profileReference": "profile-12",
  "serverReference": "server-3",
  "relationshipValid": true,
  "processModeMatchCount": 1,
  "valid": true
}
```

Response fields are exactly `type`, `correlationId`, `succeeded`, `profileReference`, `serverReference`, `relationshipValid`, `processModeMatchCount`, and `valid`.

`processModeMatchCount` is cardinality-capped:

- 0: no matching ProcessMode.
- 1: exactly one matching ProcessMode.
- 2: two or more matching ProcessModes.

`valid` is true only when the explicit pair satisfies every shared resolver rule and the capped match count is 1. Missing profiles, duplicate profile indices, missing servers, relationship mismatches, and non-unique modes return `valid=false` without a public cause token. An unexpected internal validation failure returns the same fields with `succeeded=false`, `relationshipValid=false`, `processModeMatchCount=0`, and `valid=false`.

## Isolation guarantees

Both commands are local operations on the existing `PipeOptions.CurrentUserOnly` named pipe. They do not issue or consume challenges, inspect or consume permits/JTIs, call authorization, mutate runtime status, create a proxy session, initialize a driver, require a target process, or invoke `MainController.StartAsync`.

Core reports facts only. It never selects the first profile, first server, first valid pair, any available pair, or a fallback pair.

## Runtime configuration provisioning ownership

The source-evidenced configuration store is the trusted runtime root's `data/settings.json`, loaded by `Netch.Utils.Configuration`. The legacy Netch UI/import paths create servers and profiles and persist that store; the headless Core host has no provisioning command and the published `Storage` payload contains no `settings.json`.

Therefore, if the frozen catalog is empty, discovery is implemented but runtime provisioning is missing. The responsible component is the Netch local configuration provisioning path for `data/settings.json`; the repository does not identify a current headless product team owner for that provisioning path.
