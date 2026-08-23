[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ProtectedSettingsPayload,

    [Parameter(Mandatory = $true)]
    [string]$ProtectedSettingsKeyFile,

    [string]$V2rayRuntimeFile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$approvedV2raySha256 = 'a219f435671fb214c0c530084c65e576fdc1404f40b187b5586e869d2a3e4dff'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($V2rayRuntimeFile)) {
    $V2rayRuntimeFile = Join-Path $repositoryRoot 'Storage\v2ray-sn.exe'
}
$worktreeStatus = @(& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or $worktreeStatus.Count -ne 0) {
    throw 'Production publish requires a clean Core worktree.'
}
$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Core source authority could not be resolved.'
}

function Resolve-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is required."
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-LowerSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$payloadPath = Resolve-RequiredFile -Path $ProtectedSettingsPayload -Label 'Protected settings payload'
$keyPath = Resolve-RequiredFile -Path $ProtectedSettingsKeyFile -Label 'Protected settings key'
$v2rayPath = Resolve-RequiredFile -Path $V2rayRuntimeFile -Label 'Approved v2ray-sn.exe'

if ((Get-LowerSha256 -Path $v2rayPath) -ne $approvedV2raySha256) {
    throw 'Approved v2ray-sn.exe hash verification failed.'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $resolvedOutput) {
    if (Get-ChildItem -LiteralPath $resolvedOutput -Force | Select-Object -First 1) {
        throw 'Production publish output directory must be empty.'
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
}

$hostProject = Join-Path $repositoryRoot 'NekoProxyCore.Host\NekoProxyCore.Host.csproj'
$publishArguments = @(
    'publish',
    $hostProject,
    '-c', 'Release',
    '-f', 'net6.0-windows',
    '-r', 'win-x64',
    '-p:Platform=x64',
    '--self-contained', 'false',
    "-p:NekoProtectedSettingsPayload=$payloadPath",
    "-p:NekoProtectedSettingsKeyFile=$keyPath",
    "-p:NekoV2rayRuntimeFile=$v2rayPath",
    '-o', $resolvedOutput
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Production Core publish failed.'
}

$releasedKey = Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse |
    Where-Object { $_.Name -ieq 'runtime-settings.key' } |
    Select-Object -First 1
if ($null -ne $releasedKey) {
    throw 'Production publish must not release runtime-settings.key.'
}

$plaintextSettings = Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse |
    Where-Object { $_.Name -ieq 'settings.json' } |
    Select-Object -First 1
if ($null -ne $plaintextSettings) {
    throw 'Production publish must not release settings.json.'
}

$stagedV2ray = Join-Path $resolvedOutput 'bin\v2ray-sn.exe'
if (-not (Test-Path -LiteralPath $stagedV2ray -PathType Leaf) -or
    (Get-LowerSha256 -Path $stagedV2ray) -ne $approvedV2raySha256) {
    throw 'Published bin/v2ray-sn.exe verification failed.'
}

$sourceCommitAfterPublish = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$worktreeStatusAfterPublish = @(& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0 -or
    $sourceCommitAfterPublish -ne $sourceCommit -or
    $worktreeStatusAfterPublish.Count -ne 0) {
    throw 'Source changed during production publish.'
}

$manifestPath = Join-Path $resolvedOutput 'core-manifest.json'
$files = [ordered]@{}
Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse |
    Where-Object { $_.FullName -ne $manifestPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($resolvedOutput.TrimEnd('\').Length + 1).Replace('\', '/')
        $files[$relativePath] = Get-LowerSha256 -Path $_.FullName
    }

if (-not $files.Contains('bin/v2ray-sn.exe') -or
    $files['bin/v2ray-sn.exe'] -ne $approvedV2raySha256) {
    throw 'Manifest input does not contain approved bin/v2ray-sn.exe.'
}

$requiredManifestFiles = @(
    'NekoProxyCore.exe',
    'NekoProxyCore.dll',
    'runtime-settings.nkps',
    'bin/Redirector.bin',
    'bin/nfapi.dll',
    'bin/v2ray-sn.exe'
)
foreach ($requiredManifestFile in $requiredManifestFiles) {
    if (-not $files.Contains($requiredManifestFile)) {
        throw "Manifest input is missing required runtime file: $requiredManifestFile"
    }
}

$manifest = [ordered]@{
    source_commit = $sourceCommit
    file_count = $files.Count
    neko_proxy_core_exe_hash = $files['NekoProxyCore.exe']
    neko_proxy_core_dll_hash = $files['NekoProxyCore.dll']
    protected_settings_payload_hash = $files['runtime-settings.nkps']
    redirector_bin_hash = $files['bin/Redirector.bin']
    nfapi_dll_hash = $files['bin/nfapi.dll']
    v2ray_sn_exe_hash = $files['bin/v2ray-sn.exe']
    files = $files
}

$manifestJson = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    [System.Text.UTF8Encoding]::new($false))

Write-Output "SOURCE_COMMIT=$sourceCommit"
Write-Output "FILE_COUNT=$($files.Count)"
Write-Output "V2RAY_SHA256=$approvedV2raySha256"
