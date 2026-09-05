<#
.SYNOPSIS
Checks whether a Windows workstation and checkout are ready for NekoProxyCore work.

.DESCRIPTION
Performs read-only checks for the pinned Netch 1.9.7 ancestry, expected branch,
Git remotes, required source/runtime files, build tools, optional Npcap support,
and obvious tracked secret-risk filenames. The script does not install software,
switch branches, edit source files, or change global Git configuration.

.PARAMETER RepoPath
Path to the NekoProxyCore checkout. Defaults to the repository containing this
script.

.PARAMETER AsJson
Emits a machine-readable JSON report instead of the human-readable table.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\neko-proxycore-preflight.ps1

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\neko-proxycore-preflight.ps1 -AsJson

.NOTES
Exit 0: ready. Exit 2: warnings only. Exit 1: one or more required checks failed.
#>

[CmdletBinding()]
param(
    [string]$RepoPath,
    [switch]$AsJson
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Continue'

$expectedBranch = 'feature/neko-headless'
$pinnedSha = '99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687'
$results = @()

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $RepoPath = $repositoryRoot
}

function Add-Result {
    param(
        [string]$Name,
        [ValidateSet('PASS', 'WARN', 'FAIL')]
        [string]$Status,
        [string]$Details,
        [bool]$Required = $true
    )

    $script:results += [pscustomobject]@{
        Name = $Name
        Status = $Status
        Required = $Required
        Details = $Details
    }
}

function Get-ExecutablePath {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }
    return $command.Source
}

function Invoke-GitText {
    param([string[]]$Arguments)

    $output = & git -c "safe.directory=$RepoPath" -C $RepoPath @Arguments 2>$null
    $ok = ($LASTEXITCODE -eq 0)
    return [pscustomobject]@{
        Ok = $ok
        Text = (($output | Out-String).Trim())
    }
}

function Test-GitAncestor {
    param(
        [string]$Ancestor,
        [string]$Descendant
    )

    & git -c "safe.directory=$RepoPath" -C $RepoPath merge-base --is-ancestor $Ancestor $Descendant 2>$null
    return ($LASTEXITCODE -eq 0)
}

$resolvedRepo = $null
if (Test-Path -LiteralPath $RepoPath -PathType Container) {
    $resolvedRepo = (Resolve-Path -LiteralPath $RepoPath).Path
    $RepoPath = $resolvedRepo
    Add-Result 'repository path' 'PASS' $RepoPath
}
else {
    Add-Result 'repository path' 'FAIL' "Repository not found: $RepoPath"
}

$gitPath = Get-ExecutablePath 'git'
if ($null -eq $gitPath) {
    Add-Result 'Git' 'FAIL' 'git.exe is not available on PATH'
}
else {
    Add-Result 'Git' 'PASS' $gitPath
}

if ($null -ne $resolvedRepo -and $null -ne $gitPath) {
    $inside = Invoke-GitText @('rev-parse', '--is-inside-work-tree')
    if ($inside.Ok -and $inside.Text -eq 'true') {
        Add-Result 'Git working tree' 'PASS' $RepoPath

        $branch = Invoke-GitText @('branch', '--show-current')
        if ($branch.Ok -and $branch.Text -eq $expectedBranch) {
            Add-Result 'active branch' 'PASS' $branch.Text
        }
        else {
            Add-Result 'active branch' 'FAIL' "Expected $expectedBranch; found '$($branch.Text)'"
        }

        $head = Invoke-GitText @('rev-parse', 'HEAD')
        if ($head.Ok -and (Test-GitAncestor $pinnedSha 'HEAD')) {
            Add-Result 'pinned source ancestry' 'PASS' "HEAD $($head.Text) is based on $pinnedSha"
        }
        elseif ($head.Ok) {
            Add-Result 'pinned source ancestry' 'FAIL' "HEAD $($head.Text) is not based on $pinnedSha"
        }
        else {
            Add-Result 'pinned source ancestry' 'FAIL' 'Unable to read HEAD'
        }

        $origin = Invoke-GitText @('remote', 'get-url', 'origin')
        if ($origin.Ok -and $origin.Text -match 'Valeneko-pranmong/NekoProxyCore') {
            Add-Result 'origin remote' 'PASS' $origin.Text
        }
        else {
            Add-Result 'origin remote' 'FAIL' "Unexpected origin: $($origin.Text)"
        }

        $upstream = Invoke-GitText @('remote', 'get-url', 'upstream')
        if ($upstream.Ok -and $upstream.Text -match 'netchx/netch') {
            Add-Result 'upstream remote' 'PASS' $upstream.Text
        }
        else {
            Add-Result 'upstream remote' 'WARN' 'upstream/netchx/netch is not configured' $false
        }

        $baselineRef = 'refs/remotes/origin/baseline/netch-1.9.7'
        $baseline = Invoke-GitText @('rev-parse', '--verify', $baselineRef)
        if ($baseline.Ok -and $baseline.Text -eq $pinnedSha) {
            Add-Result 'baseline remote ref' 'PASS' $baseline.Text
        }
        else {
            Add-Result 'baseline remote ref' 'FAIL' "Expected $pinnedSha; found '$($baseline.Text)'"
        }

        $featureRef = 'refs/remotes/origin/feature/neko-headless'
        $feature = Invoke-GitText @('rev-parse', '--verify', $featureRef)
        if ($feature.Ok -and (Test-GitAncestor $pinnedSha $featureRef)) {
            Add-Result 'feature remote ref' 'PASS' "$($feature.Text) is based on $pinnedSha"
        }
        else {
            Add-Result 'feature remote ref' 'FAIL' 'Remote feature branch is missing or is not based on Netch 1.9.7'
        }

        $status = & git -c "safe.directory=$RepoPath" -C $RepoPath status --porcelain=v1 2>$null
        if ($status) {
            Add-Result 'worktree' 'WARN' 'Working tree has changes; inspect them before editing or building' $false
        }
        else {
            Add-Result 'worktree' 'PASS' 'Clean'
        }

        $upstreamState = Invoke-GitText @('rev-list', '--left-right', '--count', 'HEAD...@{upstream}')
        if ($upstreamState.Ok) {
            Add-Result 'tracking state' 'PASS' "HEAD...upstream = $($upstreamState.Text)"
        }
        else {
            Add-Result 'tracking state' 'WARN' 'Current branch has no readable upstream tracking state' $false
        }
    }
    else {
        Add-Result 'Git working tree' 'FAIL' "$RepoPath is not a Git working tree"
    }
}

$requiredFiles = @(
    'LICENSE',
    'Netch.sln',
    'build.ps1',
    'Netch/Netch.csproj',
    'Netch/Program.cs',
    'Netch/Global.cs',
    'Netch/App.manifest',
    'Netch/Controllers/MainController.cs',
    'Netch/Controllers/NFController.cs',
    'Netch/Controllers/TUNController.cs',
    'Netch/Controllers/PcapController.cs',
    'Netch/Services/ModeService.cs',
    'Redirector/Redirector.vcxproj',
    'RouteHelper/RouteHelper.vcxproj',
    'Storage/nfdriver.sys',
    'Storage/tun2socks.bin',
    'Storage/stun.txt',
    'Storage/aiodns.conf'
)

if ($null -ne $resolvedRepo) {
    foreach ($relativePath in $requiredFiles) {
        $nativePath = $relativePath -replace '/', '\'
        $absolutePath = Join-Path $RepoPath $nativePath
        if (Test-Path -LiteralPath $absolutePath) {
            Add-Result "file $relativePath" 'PASS' 'Present'
        }
        else {
            Add-Result "file $relativePath" 'FAIL' 'Missing'
        }
    }
}

$dotnetPath = Get-ExecutablePath 'dotnet'
if ($null -eq $dotnetPath) {
    Add-Result '.NET SDK' 'FAIL' 'dotnet.exe is not available'
}
else {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks) {
        Add-Result '.NET SDK' 'PASS' (($sdks -join '; ').Trim())
    }
    else {
        Add-Result '.NET SDK' 'FAIL' 'dotnet exists, but no SDK is installed'
    }
}

$msbuildPath = Get-ExecutablePath 'msbuild'
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if ($null -eq $msbuildPath -and (Test-Path -LiteralPath $vswherePath)) {
    $msbuildPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
}
if ($msbuildPath) {
    Add-Result 'MSBuild' 'PASS' $msbuildPath
}
else {
    Add-Result 'MSBuild' 'FAIL' 'Install Visual Studio Build Tools 2022 with MSBuild'
}

if (Test-Path -LiteralPath $vswherePath) {
    $vcInstall = & $vswherePath -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath 2>$null
    if ($vcInstall) {
        Add-Result 'Visual C++ workload' 'PASS' (($vcInstall -join '; ').Trim())
    }
    else {
        Add-Result 'Visual C++ workload' 'FAIL' 'Install MSVC x64/x86 build tools workload'
    }
}
else {
    Add-Result 'Visual C++ workload' 'FAIL' 'Visual Studio Installer/vswhere.exe was not found'
}

$windowsSdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
$windowsSdkVersions = Get-ChildItem -Path $windowsSdkRoot -Directory -ErrorAction SilentlyContinue
if ($windowsSdkVersions) {
    Add-Result 'Windows SDK' 'PASS' (($windowsSdkVersions.Name -join '; ').Trim())
}
else {
    Add-Result 'Windows SDK' 'FAIL' 'Install a Windows 10 or Windows 11 SDK'
}

$goPath = Get-ExecutablePath 'go'
if ($null -eq $goPath) {
    $defaultGoPath = Join-Path $env:ProgramFiles 'Go\bin\go.exe'
    if (Test-Path -LiteralPath $defaultGoPath) {
        $goPath = (Resolve-Path -LiteralPath $defaultGoPath).Path
    }
}
if ($null -ne $goPath) {
    $goVersion = & $goPath version 2>$null
    Add-Result 'Go toolchain' 'PASS' "$goPath ($goVersion)"
}
else {
    Add-Result 'Go toolchain' 'FAIL' 'Install Go; Other/aiodns declares go 1.17'
}

$npcapDlls = @(
    (Join-Path $env:SystemRoot 'System32\wpcap.dll'),
    (Join-Path $env:SystemRoot 'System32\Packet.dll')
)
$missingNpcap = $npcapDlls | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missingNpcap) {
    Add-Result 'Npcap runtime' 'WARN' 'wpcap.dll/Packet.dll not found; optional for ProcessMode, required for PcapMode' $false
}
else {
    Add-Result 'Npcap runtime' 'PASS' 'wpcap.dll and Packet.dll found' $false
}

if ($null -ne $resolvedRepo) {
    $buildScript = Join-Path $RepoPath 'build.ps1'
    if (Test-Path -LiteralPath $buildScript) {
        $buildText = Get-Content -Raw -Encoding utf8 $buildScript
        if ($buildText -match 'Invoke-WebRequest') {
            Add-Result 'build reproducibility' 'WARN' 'build.ps1 downloads GeoLite2; pin URL and checksum before release' $false
        }
        else {
            Add-Result 'build reproducibility' 'PASS' 'No build-time download detected'
        }
    }

    $tracked = Invoke-GitText @('ls-files')
    if ($tracked.Ok) {
        $risky = $tracked.Text -split "`r?`n" | Where-Object {
            $_ -match '(^|/)(\.env($|\.)|settings\.json$|.*\.pfx$|.*\.key$|.*\.dmp$|.*\.log$)'
        }
        if ($risky) {
            Add-Result 'tracked secret-risk files' 'WARN' (($risky -join '; ').Trim()) $false
        }
        else {
            Add-Result 'tracked secret-risk files' 'PASS' 'No obvious secret/log/dump filenames are tracked' $false
        }
    }
}

$failCount = @($results | Where-Object { $_.Status -eq 'FAIL' }).Count
$warnCount = @($results | Where-Object { $_.Status -eq 'WARN' }).Count
$passCount = @($results | Where-Object { $_.Status -eq 'PASS' }).Count

$report = [pscustomobject]@{
    Repository = $RepoPath
    ExpectedBranch = $expectedBranch
    PinnedSha = $pinnedSha
    Pass = $passCount
    Warn = $warnCount
    Fail = $failCount
    Checks = $results
}

if ($AsJson) {
    Write-Output ($report | ConvertTo-Json -Depth 6)
}
else {
    $results | Format-Table -AutoSize
    Write-Output ''
    Write-Output ("Summary: PASS={0} WARN={1} FAIL={2}" -f $passCount, $warnCount, $failCount)
    if ($failCount -gt 0) {
        Write-Output 'BLOCKED: install or fix required items before source changes/build.'
    }
    elseif ($warnCount -gt 0) {
        Write-Output 'READY WITH WARNINGS: review warnings before release.'
    }
    else {
        Write-Output 'READY: preflight passed.'
    }
}

if ($failCount -gt 0) {
    exit 1
}
if ($warnCount -gt 0) {
    exit 2
}
exit 0
