<#
.SYNOPSIS
    Builds dist\FilesaCOChat-<version>-Setup.msi from dist\FilesaCOChat-<version>-win-x64.

.DESCRIPTION
    1. gen-wxs.py enumerates the payload folder and writes Package.wxs
       (one component per file, directory tree mirrored, stable GUIDs).
    2. wix build compresses everything into a single per-user MSI.
    3. wix msi validate runs the ICE checks (expected noise: ICE91 for a
       per-user package, ICE61-style short-name warnings from the .mui folders).

    Requires the WiX v5 CLI:  dotnet tool install --global wix --version 5.0.2
    (WiX v6/v7 ask for the OSMF licence, which this project does not accept.)
#>
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

# Version comes from src\FilesaCOChat.Core\AppInfo.cs (single source of truth).
$appInfo = Join-Path $root 'src\FilesaCOChat.Core\AppInfo.cs'
$version = [regex]::Match((Get-Content $appInfo -Raw),
    'const string Version = "([0-9]+(\.[0-9]+)*)"').Groups[1].Value
if (-not $version) { throw "could not read AppInfo.Version from $appInfo" }

$payload = Join-Path $root "dist\FilesaCOChat-$version-win-x64"
$msi = Join-Path $root "dist\FilesaCOChat-$version-Setup.msi"
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
if (-not (Test-Path $wix)) { $wix = 'wix' }

if (-not (Test-Path $payload)) {
    throw "payload folder missing: $payload (publish the app first)"
}

& python (Join-Path $here 'gen-wxs.py')
if ($LASTEXITCODE -ne 0) { throw 'gen-wxs.py failed' }

& $wix build (Join-Path $here 'Package.wxs') -arch x64 -d "SourceDir=$payload" -o $msi -sw1070 -sw1071
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

& $wix msi validate $msi -sw1076 | Out-Null

$file = Get-Item $msi
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash
'{0}  {1:N0} bytes  SHA256 {2}' -f $file.Name, $file.Length, $hash
