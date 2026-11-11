<#
.SYNOPSIS
    End-to-end test of the single-file installer: silent install to a temp dir, shortcut, launch,
    upgrade, running-app upgrade, path with a space, uninstall. Exit 0 only if every check passes.
.PARAMETER Installer
    Installer to test (default: newest installer/Output/AvasRoutingTool-Setup-*.exe).
#>
param([string]$Installer)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$script:results = New-Object System.Collections.ArrayList

# Runs $Body; a $true result passes, a string result fails with that text as detail.
function Check([string]$Name, [scriptblock]$Body) {
    $ok = $false; $detail = ''
    try {
        $r = & $Body
        if ($r -is [string]) { $detail = $r } else { $ok = [bool]$r }
    }
    catch { $detail = $_.Exception.Message }
    [void]$script:results.Add([pscustomobject]@{ Name = $Name; Ok = $ok })
    if ($ok) { Write-Host "PASS $Name" } else { Write-Host "FAIL $Name $detail" }
    return $ok
}

function Invoke-Setup([string]$Dir, [string]$LogFile) {
    $setupArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$Dir`"", '/TASKS="desktopicon"', "/LOG=`"$LogFile`"")
    $p = Start-Process -FilePath $Installer -ArgumentList $setupArgs -Wait -PassThru
    return $p.ExitCode
}

function Invoke-Uninstall([string]$Dir) {
    $u = Join-Path $Dir 'unins000.exe'
    if (-not (Test-Path -LiteralPath $u)) { return -1 }
    $p = Start-Process -FilePath $u -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    # The uninstaller hands off to a temp copy; wait for it to finish removing files
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline -and (Test-Path -LiteralPath $u)) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Seconds 1
    return $p.ExitCode
}

# Stops only processes whose image lives under $Dir (the app and its msedgewebview2 children)
function Stop-InstalledApp([string]$Dir) {
    $prefix = $Dir.TrimEnd('\') + '\'
    foreach ($name in 'AvasRoutingTool', 'msedgewebview2') {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            $path = $null
            try { $path = $_.Path } catch {}
            if ($path -and $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                try { $_.Kill() } catch {}
            }
        }
    }
    Start-Sleep -Milliseconds 800
}

function Get-DesktopLink { Join-Path ([Environment]::GetFolderPath('Desktop')) 'AVAS Routing Tool.lnk' }
function Get-StartLink   { Join-Path ([Environment]::GetFolderPath('Programs')) 'AVAS Routing Tool.lnk' }
function Get-StartFolder { Join-Path ([Environment]::GetFolderPath('Programs')) 'AVAS Routing Tool' }
function Get-LinkTarget([string]$Lnk) { (New-Object -ComObject WScript.Shell).CreateShortcut($Lnk).TargetPath }

function Start-AppAndWait([string]$Dir, [int]$Seconds) {
    $exe = Join-Path $Dir 'AvasRoutingTool.exe'
    $p = Start-Process -FilePath $exe -WorkingDirectory $Dir -PassThru
    Start-Sleep -Seconds $Seconds
    $p.Refresh()
    return $p
}

$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "avas-installer-test-$stamp"
$dirA = Join-Path $tempRoot 'AvasInstall'
$dirB = Join-Path $tempRoot 'AVAS Test Dir'
$logA = Join-Path $tempRoot 'install-a.log'
$logB = Join-Path $tempRoot 'install-b.log'
$desktopLink = Get-DesktopLink
$startLink = Get-StartLink
$startFolder = Get-StartFolder
$desktopPreexisted = Test-Path -LiteralPath $desktopLink
$startPreexisted = (Test-Path -LiteralPath $startLink) -or (Test-Path -LiteralPath $startFolder)

try {
    $null = New-Item -ItemType Directory -Force -Path $tempRoot

    if (-not $Installer) {
        $found = Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'installer\Output') -Filter 'AvasRoutingTool-Setup-*.exe' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { $Installer = $found.FullName }
    }

    $haveInstaller = Check 'installer-exists' { [bool]$Installer -and (Test-Path -LiteralPath $Installer) }
    if (-not $haveInstaller) { throw 'abort' }

    # Safety: do not run over a real installation (same AppId) or pre-existing shortcuts
    $iss = Join-Path $RepoRoot 'installer\AvasRoutingTool.iss'
    $guard = Check 'no-existing-install' {
        if ($desktopPreexisted -or $startPreexisted) { return 'AVAS Routing Tool shortcut already exists on this PC; uninstall the real copy first' }
        $m = Select-String -LiteralPath $iss -Pattern '^AppId=\{\{?([0-9A-Fa-f-]+)\}' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($m) {
            $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$($m.Matches[0].Groups[1].Value)}_is1"
            if (Test-Path $key) { return 'AVAS Routing Tool is already installed (registry key found); uninstall it first' }
        }
        $true
    }
    if (-not $guard) { throw 'abort' }

    # ---------------- Phase A: temp dir without a space ----------------
    Check 'install-ok' { $c = Invoke-Setup $dirA $logA; if ($c -eq 0) { $true } else { "exit code $c" } } | Out-Null
    Check 'exe-present'      { Test-Path -LiteralPath (Join-Path $dirA 'AvasRoutingTool.exe') } | Out-Null
    Check 'libvlc-present'   { Test-Path -LiteralPath (Join-Path $dirA 'libvlc\win-x64\libvlc.dll') } | Out-Null
    Check 'webview2-present' { Test-Path -LiteralPath (Join-Path $dirA 'WebView2Runtime\msedgewebview2.exe') } | Out-Null
    Check 'defaults-shipped' {
        $t = Get-Content -LiteralPath (Join-Path $dirA 'appsettings.json') -Raw
        ($t -match '"BavmUrl":\s*"http://localhost:80"') -and ($t -match '"RestPort":\s*8080') -and ($t -match '"ShowLogs":\s*false') -and ($t -match '"ShowAdvanced":\s*false')
    } | Out-Null
    Check 'desktop-shortcut' {
        (Test-Path -LiteralPath $desktopLink) -and ((Get-LinkTarget $desktopLink) -ieq (Join-Path $dirA 'AvasRoutingTool.exe'))
    } | Out-Null
    Check 'start-menu-shortcut' {
        (Test-Path -LiteralPath $startLink) -and ((Get-LinkTarget $startLink) -ieq (Join-Path $dirA 'AvasRoutingTool.exe'))
    } | Out-Null
    Check 'launches' {
        $p = Start-AppAndWait $dirA 10
        try { -not $p.HasExited } finally { Stop-InstalledApp $dirA }
    } | Out-Null

    Check 'upgrade-keeps-settings' {
        $f = Join-Path $dirA 'appsettings.json'
        $t = Get-Content -LiteralPath $f -Raw
        Set-Content -LiteralPath $f -Value ($t -replace '"BavmUrl":\s*"[^"]*"', '"BavmUrl": "http://TEST-KEEP:80"') -Encoding UTF8
        $c = Invoke-Setup $dirA $logA
        if ($c -ne 0) { return "upgrade exit code $c" }
        (Get-Content -LiteralPath $f -Raw) -match 'http://TEST-KEEP:80'
    } | Out-Null

    # Leave user data and a log behind so uninstall must remove them
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $dirA 'WebView2_UserData\Default')
    Set-Content -LiteralPath (Join-Path $dirA 'WebView2_UserData\Default\marker.txt') -Value 'x'
    Set-Content -LiteralPath (Join-Path $dirA 'app.log') -Value 'log'

    Check 'uninstall-ok' {
        $c = Invoke-Uninstall $dirA
        if ($c -ne 0) { return "uninstall exit code $c" }
        $left = @()
        if (Test-Path -LiteralPath $dirA) { $left = @(Get-ChildItem -LiteralPath $dirA -Force | Where-Object { $_.Name -ne 'appsettings.json' }) }
        if ($left.Count -gt 0) { return ('left behind: ' + (($left | ForEach-Object Name) -join ', ')) }
        if (Test-Path -LiteralPath $desktopLink) { return 'desktop shortcut still present' }
        if (Test-Path -LiteralPath $startLink) { return 'start menu shortcut still present' }
        if (Test-Path -LiteralPath $startFolder) { return 'start menu folder still present' }
        $true
    } | Out-Null
    Check 'uninstall-removes-userdata-and-log' {
        -not (Test-Path -LiteralPath (Join-Path $dirA 'WebView2_UserData')) -and -not (Test-Path -LiteralPath (Join-Path $dirA 'app.log'))
    } | Out-Null
    Check 'uninstall-keeps-settings-when-silent' {
        (Test-Path -LiteralPath (Join-Path $dirA 'appsettings.json')) -and ((Get-Content -LiteralPath (Join-Path $dirA 'appsettings.json') -Raw) -match 'TEST-KEEP')
    } | Out-Null

    # ---------------- Phase B: path with a space, app running during upgrade ----------------
    Check 'space-install-ok' { $c = Invoke-Setup $dirB $logB; if ($c -eq 0) { $true } else { "exit code $c" } } | Out-Null
    Check 'space-desktop-shortcut' {
        (Test-Path -LiteralPath $desktopLink) -and ((Get-LinkTarget $desktopLink) -ieq (Join-Path $dirB 'AvasRoutingTool.exe'))
    } | Out-Null
    Check 'space-launches' {
        $p = Start-AppAndWait $dirB 10
        try { -not $p.HasExited } finally { Stop-InstalledApp $dirB }
    } | Out-Null
    Check 'upgrade-while-app-running' {
        $p = Start-AppAndWait $dirB 8
        if ($p.HasExited) { return 'app did not stay running before upgrade' }
        $c = Invoke-Setup $dirB $logB
        Start-Sleep -Seconds 2
        $p.Refresh()
        $stillRunning = -not $p.HasExited
        Stop-InstalledApp $dirB
        if ($c -ne 0) { return "upgrade exit code $c" }
        if ($stillRunning) { return 'installer exited 0 but the app was still running' }
        $true
    } | Out-Null
    Check 'space-uninstall-ok' {
        $c = Invoke-Uninstall $dirB
        if ($c -ne 0) { return "uninstall exit code $c" }
        $left = @()
        if (Test-Path -LiteralPath $dirB) { $left = @(Get-ChildItem -LiteralPath $dirB -Force | Where-Object { $_.Name -ne 'appsettings.json' }) }
        if ($left.Count -gt 0) { return ('left behind: ' + (($left | ForEach-Object Name) -join ', ')) }
        -not (Test-Path -LiteralPath $desktopLink) -and -not (Test-Path -LiteralPath $startLink)
    } | Out-Null
}
catch { if ($_.Exception.Message -ne 'abort') { Write-Host "FAIL unexpected-error $($_.Exception.Message)"; [void]$script:results.Add([pscustomobject]@{ Name = 'unexpected-error'; Ok = $false }) } }
finally {
    # Always clean up: stop app, uninstall anything left, remove shortcuts we created, delete temp dirs
    foreach ($d in $dirA, $dirB) {
        try { Stop-InstalledApp $d } catch {}
        try { if (Test-Path -LiteralPath (Join-Path $d 'unins000.exe')) { Invoke-Uninstall $d | Out-Null } } catch {}
    }
    try {
        if (-not $desktopPreexisted -and (Test-Path -LiteralPath $desktopLink)) { Remove-Item -LiteralPath $desktopLink -Force }
        if (-not $startPreexisted) {
            if (Test-Path -LiteralPath $startLink) { Remove-Item -LiteralPath $startLink -Force }
            if (Test-Path -LiteralPath $startFolder) { Remove-Item -LiteralPath $startFolder -Recurse -Force }
        }
    } catch {}
    try { if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force } } catch { Write-Host "WARN could not remove $tempRoot" }
}

$failed = @($script:results | Where-Object { -not $_.Ok })
Write-Host ("{0} checks, {1} failed" -f $script:results.Count, $failed.Count)
if ($script:results.Count -eq 0 -or $failed.Count -gt 0) { exit 1 }
exit 0
