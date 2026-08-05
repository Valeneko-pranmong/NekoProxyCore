<#
.SYNOPSIS
Publishes and runs the sanitized ProcessMode integration runner with win-x64 runtime assets.

.DESCRIPTION
This script never prints runtime configuration values. It publishes the official runner,
verifies that all RID-specific Windows runtime assemblies were staged, copies only
approved runtime directories into a temporary directory, runs the lifecycle, and removes the
entire temporary mirror in a finally block.

A runner exit code of 0 proves lifecycle/local-SOCKS readiness only. Historical Step D criteria
are archived in docs/archive/step-d/tester-handoff.md; current release gates are documented in
docs/current/core-release-handoff.md.
#>

[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$ProcessName = 'pso2.exe',
    [ValidatePattern('^profile-[0-9]+$')]
    [string]$ProfileReference = 'profile-0',
    [ValidatePattern('^server-[0-9]+$')]
    [string]$ServerReference = 'server-0',
    [ValidateRange(0, 900)]
    [int]$TrafficWindowSeconds = 300,
    [switch]$PrepareOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$RuntimeRoot = Join-Path $repositoryRoot 'Original setting'
$projectPath = Join-Path $repositoryRoot 'NekoProxyCore.IntegrationRunner\NekoProxyCore.IntegrationRunner.csproj'
$publishRoot = Join-Path $repositoryRoot 'NekoProxyCore.IntegrationRunner\bin\Release\net6.0-windows\win-x64\publish'
$legacyOutput = Join-Path $repositoryRoot 'Netch\bin\x64\Release'
$windowsRuntimeAssetDirectory = Join-Path $legacyOutput 'runtimes\win\lib\net6.0'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("NekoProcessModeIntegration-" + [Guid]::NewGuid().ToString('N'))
$runtimeDirectories = @('data', 'mode', 'bin', 'i18n')
$allowedOutputPattern = '^(CONFIG |EVENT |START |STEADY |SOCKS_PROBE |TRAFFIC_WINDOW |STOP |STOP_AGAIN |CLEANUP |TRAFFIC_GATE |FATAL )'
$runnerTimeoutMilliseconds = ($TrafficWindowSeconds + 180) * 1000

function Assert-FileExists {
    param([string]$Path, [string]$Label)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing. Build the verified Release artifacts before running integration."
    }
}

function Assert-DirectoryExists {
    param([string]$Path, [string]$Label)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label is missing."
    }
}

try {
    Assert-FileExists $projectPath 'Integration runner project'
    Assert-FileExists (Join-Path $repositoryRoot 'NekoProxyCore.Core\bin\Release\net6.0\NekoProxyCore.Core.dll') 'Core Release artifact'
    Assert-FileExists (Join-Path $repositoryRoot 'NekoProxyCore.Windows\bin\Release\net6.0\NekoProxyCore.Windows.dll') 'Windows Release artifact'
    Assert-FileExists (Join-Path $repositoryRoot 'NekoProxyCore.Legacy\bin\x64\Release\net6.0-windows\NekoProxyCore.Legacy.dll') 'Legacy Windows Release artifact'
    Assert-FileExists (Join-Path $legacyOutput 'Netch.dll') 'Netch Release artifact'
    Assert-DirectoryExists $windowsRuntimeAssetDirectory 'RID-specific Windows runtime asset directory'
    $windowsRuntimeAssets = @(Get-ChildItem -LiteralPath $windowsRuntimeAssetDirectory -File -Filter '*.dll')
    if ($windowsRuntimeAssets.Count -eq 0) {
        throw 'No RID-specific Windows runtime assets were found.'
    }

    & dotnet restore $projectPath -r win-x64 --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Integration runner restore failed with exit code $LASTEXITCODE."
    }

    & dotnet publish $projectPath -c Release -r win-x64 --self-contained false --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Integration runner publish failed with exit code $LASTEXITCODE."
    }

    foreach ($runtimeAsset in $windowsRuntimeAssets) {
        $stagedRuntimeAsset = Join-Path $publishRoot $runtimeAsset.Name
        Assert-FileExists $stagedRuntimeAsset "Staged Windows runtime asset $($runtimeAsset.Name)"
        $sourceRuntimeHash = (Get-FileHash -LiteralPath $runtimeAsset.FullName -Algorithm SHA256).Hash
        $stagedRuntimeHash = (Get-FileHash -LiteralPath $stagedRuntimeAsset -Algorithm SHA256).Hash
        if ($sourceRuntimeHash -ne $stagedRuntimeHash) {
            throw "Published runner contains the wrong Windows runtime asset: $($runtimeAsset.Name)."
        }
    }

    Write-Output "PREPARE runtime=win-x64 windowsRuntimeAssets=verified count=$($windowsRuntimeAssets.Count)"
    if ($PrepareOnly) {
        Write-Output 'PREPARE_ONLY result=ready'
        exit 0
    }

    Assert-DirectoryExists $RuntimeRoot 'Approved runtime root'
    foreach ($directory in $runtimeDirectories) {
        Assert-DirectoryExists (Join-Path $RuntimeRoot $directory) "Runtime $directory directory"
    }

    $normalizedProcessName = [System.IO.Path]::GetFileNameWithoutExtension($ProcessName)
    $targetProcesses = @(Get-Process -Name $normalizedProcessName -ErrorAction SilentlyContinue)
    if ($targetProcesses.Count -eq 0) {
        Write-Output 'PRECONDITION process=missing'
        exit 20
    }
    Write-Output "PRECONDITION process=running count=$($targetProcesses.Count)"

    $driver = Get-Service -Name 'netfilter2' -ErrorAction SilentlyContinue
    if ($null -eq $driver -or $driver.Status -ne 'Running') {
        Write-Output 'PRECONDITION netfilter2=not-running'
        exit 21
    }
    Write-Output 'PRECONDITION netfilter2=running'

    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $temporaryRoot -Recurse -Force
    foreach ($directory in $runtimeDirectories) {
        Copy-Item -LiteralPath (Join-Path $RuntimeRoot $directory) -Destination (Join-Path $temporaryRoot $directory) -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $temporaryRoot 'logging') -Force | Out-Null

    $runnerPath = Join-Path $temporaryRoot 'NekoProxyCore.IntegrationRunner.exe'
    Assert-FileExists $runnerPath 'Published integration runner'

    $rawOutputPath = Join-Path $temporaryRoot 'runner.raw.log'
    $rawErrorPath = Join-Path $temporaryRoot 'runner.raw.err.log'
    $runnerProcess = Start-Process -FilePath $runnerPath `
        -ArgumentList @($ProcessName, $ProfileReference, $ServerReference, $TrafficWindowSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)) `
        -WorkingDirectory $temporaryRoot `
        -NoNewWindow `
        -PassThru `
        -RedirectStandardOutput $rawOutputPath `
        -RedirectStandardError $rawErrorPath
    if (-not $runnerProcess.WaitForExit($runnerTimeoutMilliseconds)) {
        & taskkill.exe /PID $runnerProcess.Id /T /F 2>&1 | Out-Null
        $runnerProcess.WaitForExit(10000) | Out-Null
        Write-Output 'TIMEOUT runner=exceeded'
        exit 22
    }

    # The timed overload can return before redirected stream handling and the managed
    # Process object have finalized. Complete the wait and refresh before reading ExitCode;
    # otherwise Windows PowerShell can expose a null value that `exit` coerces to success.
    $runnerProcess.WaitForExit()
    $runnerProcess.Refresh()
    [int]$runnerExitCode = $runnerProcess.ExitCode

    Get-Content -LiteralPath $rawOutputPath -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_ -match $allowedOutputPattern) {
            Write-Output $_
        }
    }
    Write-Output "RUNNER exit=$runnerExitCode"
    exit $runnerExitCode
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
