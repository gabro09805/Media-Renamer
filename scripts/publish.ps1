<#
 Builds the standalone Windows distribution of MediaRenamer.
   .\scripts\publish.ps1                 -> dist\MediaRenamer (self-contained) + dist\MediaRenamer-portable.zip
 If Inno Setup (iscc.exe) is installed, it also builds dist\MediaRenamer-Setup.exe.
 Use -Runtime linux-x64 to publish for Linux (no installer, no portable zip).
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "dist\MediaRenamer"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root "src\MediaRenamer.App\MediaRenamer.App.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

if ($Runtime -notlike "win-*") { Write-Host "Published to $out"; return }

# Portable: the "portable.txt" marker makes the app keep config/cache in a local "userdata" folder.
$portable = Join-Path $root "dist\MediaRenamer-portable"
if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
Copy-Item $out $portable -Recurse
Set-Content (Join-Path $portable "portable.txt") "Remove this file to store settings in %LOCALAPPDATA%\MediaRenamer."
$zip = Join-Path $root "dist\MediaRenamer-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $portable "*") -DestinationPath $zip
Remove-Item $portable -Recurse -Force
Write-Host "Portable: $zip"

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($iscc) { $isccPath = $iscc.Source }
else {
    $isccPath = @("$env:ProgramFiles\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($isccPath) {
    & $isccPath "/DAppVersion=$Version" (Join-Path $root "installer\MediaRenamer.iss")
    Write-Host "Installer: dist\MediaRenamer-Setup.exe"
}
else { Write-Host "Inno Setup not found: installer skipped (https://jrsoftware.org/isinfo.php)." }
