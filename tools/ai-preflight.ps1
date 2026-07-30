[CmdletBinding()]
param(
    [string]$RepoPath,
    [switch]$AsJson
)

$ErrorActionPreference = 'SilentlyContinue'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = Split-Path -Parent $scriptRoot
}
$PinnedSha = '99480e99c3f5f4b0f6c4a32fdbbb4911be2a3687'
$ExpectedBranch = 'feature/neko-headless'
$Results = New-Object 'System.Collections.Generic.List[object]'

function Add-Check {
    param(
        [string]$Name,
        [ValidateSet('PASS', 'WARN', 'FAIL')]
        [string]$Status,
        [string]$Details,
        [bool]$Required = $true
    )

    $null = $Results.Add([pscustomobject]@{
        Name     = $Name
        Status   = $Status
        Required = $Required
        Details  = $Details
    })
}

function Invoke-Git {
    param([string[]]$Arguments)

    $output = & git -C $RepoPath @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return ($output -join "`n").Trim()
}

function Test-GitAncestry {
    param(
        [string]$Ancestor,
        [string]$Descendant
    )

    & git -C $RepoPath merge-base --is-ancestor $Ancestor $Descendant 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Get-CommandPath {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }
    return $command.Source
}

if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) {
    Add-Check 'repository' 'FAIL' "Repository not found: $RepoPath"
}
else {
    $RepoPath = (Resolve-Path -LiteralPath $RepoPath).Path
    $gitPath = Get-CommandPath 'git'
    if ($null -eq $gitPath) {
        Add-Check 'git' 'FAIL' 'git.exe is not available on PATH'
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $RepoPath '.git'))) {
        Add-Check 'git repository' 'FAIL' "$RepoPath is not a Git working tree"
    }
    else {
        Add-Check 'git' 'PASS' $gitPath
        Add-Check 'git repository' 'PASS' $RepoPath

        $branch = Invoke-Git @('branch', '--show-current')
        if ($branch -eq $ExpectedBranch) {
            Add-Check 'active branch' 'PASS' $branch
        }
        else {
            Add-Check 'active branch' 'FAIL' "Expected $ExpectedBranch; found '$branch'"
        }

        $head = Invoke-Git @('rev-parse', 'HEAD')
        if ($null -ne $head -and (Test-GitAncestry $PinnedSha 'HEAD')) {
            Add-Check 'pinned source' 'PASS' "HEAD $head is based on $PinnedSha"
        }
        elseif ($null -ne $head) {
            Add-Check 'pinned source' 'FAIL' "HEAD $head is not based on $PinnedSha"
        }
        else {
            Add-Check 'pinned source' 'FAIL' 'Unable to read HEAD'
        }

        $origin = Invoke-Git @('remote', 'get-url', 'origin')
        if ($origin -match 'Valeneko-pranmong/NekoProxyCore') {
            Add-Check 'origin remote' 'PASS' $origin
        }
        else {
            Add-Check 'origin remote' 'FAIL' "Unexpected origin: $origin"
        }

        $upstream = Invoke-Git @('remote', 'get-url', 'upstream')
        if ($upstream -match 'netchx/netch') {
            Add-Check 'upstream remote' 'PASS' $upstream
        }
        else {
            Add-Check 'upstream remote' 'WARN' 'upstream/netchx/netch is not configured' $false
        }

        foreach ($ref in @(
                'refs/remotes/origin/baseline/netch-1.9.7',
                'refs/remotes/origin/feature/neko-headless'
            )) {
            $refSha = Invoke-Git @('rev-parse', '--verify', $ref)
            $refIsValid = $false
            if ($ref -like '*baseline/netch-1.9.7') {
                $refIsValid = ($refSha -eq $PinnedSha)
            }
            elseif ($null -ne $refSha) {
                $refIsValid = Test-GitAncestry $PinnedSha $ref
            }
            if ($refIsValid) {
                Add-Check "remote ref $ref" 'PASS' $refSha
            }
            else {
                Add-Check "remote ref $ref" 'FAIL' "Expected $PinnedSha; found '$refSha'"
            }
        }

        $status = & git -C $RepoPath status --porcelain=v1 2>$null
        if ($status) {
            Add-Check 'worktree' 'WARN' 'Working tree has changes; review before editing' $false
        }
        else {
            Add-Check 'worktree' 'PASS' 'Clean'
        }
    }
}

$requiredFiles = @(
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
    'Storage/nfdriver.sys',
    'Storage/tun2socks.bin',
    'Storage/stun.txt',
    'Storage/aiodns.conf'
)

foreach ($relativePath in $requiredFiles) {
    $absolutePath = Join-Path $RepoPath ($relativePath -replace '/', '\')
    if (Test-Path -LiteralPath $absolutePath) {
        Add-Check "file $relativePath" 'PASS' 'Present'
    }
    else {
        Add-Check "file $relativePath" 'FAIL' 'Missing'
    }
}

$dotnetPath = Get-CommandPath 'dotnet'
if ($null -eq $dotnetPath) {
    Add-Check '.NET SDK' 'FAIL' 'dotnet.exe is not available'
}
else {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks) {
        Add-Check '.NET SDK' 'PASS' (($sdks -join '; ').Trim())
    }
    else {
        Add-Check '.NET SDK' 'FAIL' 'dotnet exists, but no SDK is installed; only runtime may be present'
    }
}

$msbuildPath = Get-CommandPath 'msbuild'
if ($null -eq $msbuildPath) {
    $msbuildCandidates = @(
        (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe')
    )
    foreach ($candidate in $msbuildCandidates) {
        $found = Get-ChildItem -Path $candidate -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $found) {
            $msbuildPath = $found.FullName
            break
        }
    }
}
if ($null -ne $msbuildPath) {
    Add-Check 'MSBuild' 'PASS' $msbuildPath
}
else {
    Add-Check 'MSBuild' 'FAIL' 'MSBuild.exe or Visual Studio Build Tools was not found'
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path -LiteralPath $vswherePath) {
    $vcInstall = & $vswherePath -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath 2>$null
    if ($vcInstall) {
        Add-Check 'Visual C++ workload' 'PASS' (($vcInstall -join '; ').Trim())
    }
    else {
        Add-Check 'Visual C++ workload' 'FAIL' 'Visual Studio found, but C++ x64 workload was not detected'
    }
}
else {
    Add-Check 'Visual C++ workload' 'FAIL' 'vswhere.exe was not found'
}

$windowsKitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
$windowsKit = Get-ChildItem -Path $windowsKitRoot -Directory -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $windowsKit) {
    Add-Check 'Windows SDK' 'PASS' $windowsKit.FullName
}
else {
    Add-Check 'Windows SDK' 'FAIL' 'Windows 10/11 SDK include directory was not found'
}

$goPath = Get-CommandPath 'go'
if ($null -ne $goPath) {
    $goVersion = & go version 2>$null
    Add-Check 'Go toolchain' 'PASS' "$goPath ($goVersion)"
}
else {
    Add-Check 'Go toolchain' 'FAIL' 'go.exe is not available; Other/aiodns and Other/v2ray-sn need Go'
}

$npcapDlls = @(
    (Join-Path ${env:SystemRoot} 'System32\wpcap.dll'),
    (Join-Path ${env:SystemRoot} 'System32\Packet.dll')
)
$missingNpcap = $npcapDlls | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missingNpcap) {
    Add-Check 'Npcap runtime' 'WARN' 'wpcap.dll/Packet.dll not found in System32; only required for PcapMode' $false
}
else {
    Add-Check 'Npcap runtime' 'PASS' 'wpcap.dll and Packet.dll found' $false
}

$buildScript = Join-Path $RepoPath 'build.ps1'
if (Test-Path -LiteralPath $buildScript) {
    $buildText = Get-Content -Raw -Encoding utf8 $buildScript
    if ($buildText -match 'Invoke-WebRequest') {
        Add-Check 'build reproducibility' 'WARN' 'build.ps1 downloads GeoLite2 during build; verify URL and checksum before release' $false
    }
    else {
        Add-Check 'build reproducibility' 'PASS' 'No build-time download detected'
    }
}

$failCount = @($Results | Where-Object { $_.Status -eq 'FAIL' }).Count
$warnCount = @($Results | Where-Object { $_.Status -eq 'WARN' }).Count
$passCount = @($Results | Where-Object { $_.Status -eq 'PASS' }).Count

$summary = [pscustomobject]@{
    Repository = $RepoPath
    Branch = $ExpectedBranch
    PinnedSha = $PinnedSha
    Pass = $passCount
    Warn = $warnCount
    Fail = $failCount
    Checks = @($Results)
}

if ($AsJson) {
    $json = ConvertTo-Json -InputObject $summary -Depth 6
    Write-Output $json
}
else {
    $Results | Format-Table -AutoSize
    Write-Output ''
    Write-Output ("Summary: PASS={0} WARN={1} FAIL={2}" -f $passCount, $warnCount, $failCount)
    if ($failCount -gt 0) {
        Write-Output 'Preflight blocked: install/fix required items before modifying source or building.'
    }
    elseif ($warnCount -gt 0) {
        Write-Output 'Preflight usable with warnings: review warnings before release.'
    }
    else {
        Write-Output 'Preflight passed.'
    }
}

if ($failCount -gt 0) {
    exit 1
}
if ($warnCount -gt 0) {
    exit 2
}
exit 0
