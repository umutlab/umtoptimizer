# UMToptimizer 4 — tek komutla test et + derle + setup üret
#   .\build.ps1                 → dist\UMToptimizer-Setup-4.0.0.exe (+ .sha256)
#   .\build.ps1 -Version 4.1.0
#   .\build.ps1 -NoSetup        → yalnızca test + publish (Inno Setup gerekmez)
# Gereksinim: .NET 10 SDK, Inno Setup 6   (winget install Microsoft.DotNet.SDK.10 JRSoftware.InnoSetup)
param([string]$Version = '4.0.0', [switch]$SkipTests, [switch]$NoSetup)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Geçersiz sürüm: $Version (örnek: 4.0.0)" }

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw '.NET 10 SDK bulunamadı: winget install Microsoft.DotNet.SDK.10' }
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $NoSetup -and -not $iscc) { throw 'Inno Setup 6 bulunamadı: winget install JRSoftware.InnoSetup  (ya da -NoSetup kullanın)' }

if (-not $SkipTests) {
    Write-Host '==> Testler (xUnit + PowerShell betik testleri)' -ForegroundColor Cyan
    & $dotnet test "$root\tests\UMToptimizer.Tests\UMToptimizer.Tests.csproj" --nologo -v q
    if ($LASTEXITCODE) { throw 'C# testleri başarısız; setup üretilmedi.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$root\tests\WindowsScript.Tests.ps1"
    if ($LASTEXITCODE) { throw 'PowerShell testleri başarısız; setup üretilmedi.' }
}

Write-Host "==> Yayımlanıyor (self-contained, tek dosya, win-x64) v$Version" -ForegroundColor Cyan
Remove-Item "$root\publish" -Recurse -Force -ErrorAction SilentlyContinue
& $dotnet publish "$root\src\UMToptimizer\UMToptimizer.csproj" -c Release -o "$root\publish" "-p:Version=$Version" --nologo
if ($LASTEXITCODE) { throw 'dotnet publish başarısız.' }
if ($NoSetup) { Write-Host "==> Hazır: $root\publish\UMToptimizer.exe" -ForegroundColor Green; return }

Write-Host '==> Setup derleniyor (Inno Setup)' -ForegroundColor Cyan
& $iscc /Q "/DAppVersion=$Version" "$root\installer\UMToptimizer.iss"
if ($LASTEXITCODE) { throw 'ISCC başarısız.' }

# İndirenler dosyanın bozulmadığını doğrulayabilsin diye SHA-256 özeti yanına yazılır.
$setup = Get-Item "$root\dist\UMToptimizer-Setup-$Version.exe"
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$($setup.FullName).sha256" -Value "$hash  $($setup.Name)" -Encoding ASCII
Write-Host ("==> Hazır: {0}  ({1:N1} MB)`n    SHA-256: {2}" -f $setup.FullName, ($setup.Length / 1MB), $hash) -ForegroundColor Green
