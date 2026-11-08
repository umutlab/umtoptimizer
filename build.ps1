# UMToptimizer 3 — tek komutla derle + setup üret
#   .\build.ps1                 → dist\UMToptimizer-Setup-3.0.0.exe
#   .\build.ps1 -Version 2.1.0
# Gereksinim: .NET 10 SDK, Inno Setup 6   (winget install Microsoft.DotNet.SDK.10 JRSoftware.InnoSetup)
param([string]$Version = '3.0.0', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not (Test-Path $dotnet)) { throw '.NET 10 SDK bulunamadı: winget install Microsoft.DotNet.SDK.10' }
if (-not $iscc) { throw 'Inno Setup 6 bulunamadı: winget install JRSoftware.InnoSetup' }

if (-not $SkipTests) {
    Write-Host '==> Testler (xUnit + PowerShell betik testleri)' -ForegroundColor Cyan
    & $dotnet test "$root\tests\UMToptimizer.Tests\UMToptimizer.Tests.csproj" --nologo -v q
    if ($LASTEXITCODE) { throw 'C# testleri başarısız; setup üretilmedi.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$root\tests\WindowsScript.Tests.ps1"
    if ($LASTEXITCODE) { throw 'PowerShell testleri başarısız; setup üretilmedi.' }
}

Write-Host "==> Yayımlanıyor (self-contained, tek dosya, win-x64) v$Version" -ForegroundColor Cyan
Remove-Item "$root\publish" -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "$root\src\UMToptimizer\UMToptimizer.csproj" -c Release -o "$root\publish" -p:Version=$Version --nologo
if ($LASTEXITCODE) { throw 'dotnet publish başarısız.' }

Write-Host '==> Setup derleniyor (Inno Setup)' -ForegroundColor Cyan
& $iscc /Q "/DAppVersion=$Version" "$root\installer\UMToptimizer.iss"
if ($LASTEXITCODE) { throw 'ISCC başarısız.' }

$setup = Get-Item "$root\dist\UMToptimizer-Setup-$Version.exe"
Write-Host ("==> Hazır: {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
