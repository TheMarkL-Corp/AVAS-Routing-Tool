<#
.SYNOPSIS
    Stages the AVAS Routing Tool and (when installer/AvasRoutingTool.iss exists) builds the single-file installer.
.PARAMETER SkipPublish
    Reuse an existing staging folder instead of running dotnet publish.
.PARAMETER Staging
    Staging folder (default: publish/installer-staging).
#>
param(
    [switch]$SkipPublish,
    [string]$Staging
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Csproj   = Join-Path $RepoRoot 'src\AvasRoutingTool\AvasRoutingTool.csproj'

function Get-AppVersion {
    $xml = [xml](Get-Content -LiteralPath $Csproj -Raw)
    $v = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $v) { throw "No <Version> found in $Csproj" }
    return "$v".Trim()
}

function Find-Iscc {
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe') }
    if ($env:LOCALAPPDATA)        { $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe') }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}

try {
    if (-not $Staging) { $Staging = Join-Path $RepoRoot 'publish\installer-staging' }
    elseif (-not [System.IO.Path]::IsPathRooted($Staging)) { $Staging = Join-Path (Get-Location).Path $Staging }
    $Staging = [System.IO.Path]::GetFullPath($Staging).TrimEnd('\', '/')

    # Safety: staging is deleted recursively, so it must be a child folder strictly inside <repo>\publish
    $publishRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'publish')).TrimEnd('\', '/')
    if (-not $Staging.StartsWith($publishRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unsafe staging path '$Staging': it must be a folder inside $publishRoot"
    }

    # 1. Version
    $version = Get-AppVersion
    Write-Host "[1/8] App version: $version"

    # 2. WebView2 runtime cache
    $runtimeSrc = Join-Path $RepoRoot 'installer\vendor\WebView2Runtime'
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeSrc 'msedgewebview2.exe'))) {
        throw "WebView2 runtime missing at installer/vendor/WebView2Runtime/msedgewebview2.exe - see installer/vendor/README.md"
    }
    Write-Host "[2/8] WebView2 runtime cache found"

    # 3. Publish
    if ($SkipPublish) {
        if (-not (Test-Path -LiteralPath $Staging)) { throw "-SkipPublish given but staging folder does not exist: $Staging" }
        Write-Host "[3/8] Skipping publish, reusing $Staging"
    } else {
        if (Test-Path -LiteralPath $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
        Write-Host "[3/8] Publishing to $Staging"
        & dotnet publish $Csproj -c Release -r win-x64 --self-contained true -o $Staging
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
    }

    # 4. Copy runtime
    $runtimeDst = Join-Path $Staging 'WebView2Runtime'
    if (Test-Path -LiteralPath $runtimeDst) { Remove-Item -LiteralPath $runtimeDst -Recurse -Force }
    Copy-Item -LiteralPath $runtimeSrc -Destination $runtimeDst -Recurse -Force
    Write-Host "[4/8] Copied WebView2 runtime"

    # 5. Shipped defaults
    $settingsSrc = Join-Path $RepoRoot 'src\AvasRoutingTool\appsettings.json'
    if (-not (Test-Path -LiteralPath $settingsSrc)) { throw "Missing $settingsSrc" }
    Copy-Item -LiteralPath $settingsSrc -Destination (Join-Path $Staging 'appsettings.json') -Force
    Write-Host "[5/8] Wrote shipped appsettings.json"

    # 6. Remove user data and logs
    $userData = Join-Path $Staging 'WebView2_UserData'
    if (Test-Path -LiteralPath $userData) { Remove-Item -LiteralPath $userData -Recurse -Force }
    $appLog = Join-Path $Staging 'app.log'
    if (Test-Path -LiteralPath $appLog) { Remove-Item -LiteralPath $appLog -Force }
    Write-Host "[6/8] Removed WebView2_UserData and app.log"

    # 7. Assertions
    foreach ($rel in 'AvasRoutingTool.exe', 'libvlc\win-x64\libvlc.dll', 'WebView2Runtime\msedgewebview2.exe') {
        if (-not (Test-Path -LiteralPath (Join-Path $Staging $rel))) { throw "Staging is missing $rel" }
    }
    foreach ($rel in 'WebView2_UserData', 'app.log') {
        if (Test-Path -LiteralPath (Join-Path $Staging $rel)) { throw "Staging must not contain $rel" }
    }
    Write-Host "[7/8] Staging verified: $Staging"

    # 8. Inno Setup
    $iss = Join-Path $RepoRoot 'installer\AvasRoutingTool.iss'
    if (-not (Test-Path -LiteralPath $iss)) {
        Write-Host "[8/8] installer/AvasRoutingTool.iss not found, skipping installer compile"
    } else {
        $iscc = Find-Iscc
        if (-not $iscc) { throw "ISCC.exe not found (install Inno Setup 6 or add it to PATH)" }
        Write-Host "[8/8] Compiling installer with $iscc"
        & $iscc "/DAppVersion=$version" "/DSourceDir=$Staging" $iss
        if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

        $outDir = Join-Path $RepoRoot 'installer\Output'
        $setupName = "AvasRoutingTool-Setup-$version.exe"
        $setup = Get-Item -LiteralPath (Join-Path $outDir $setupName) -ErrorAction SilentlyContinue
        if (-not $setup) { throw "ISCC reported success but $setupName was not found in $outDir" }

        if ($env:AVAS_SIGN_CMD) {
            $signCmd = [string]::Format($env:AVAS_SIGN_CMD, $setup.FullName)
            Write-Host "Signing $($setup.Name)"
            & cmd.exe /c $signCmd
            if ($LASTEXITCODE -ne 0) { throw "Signing failed (exit $LASTEXITCODE)" }
        }
        Write-Host "Installer: $($setup.FullName)"
    }
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)"
    exit 1
}
