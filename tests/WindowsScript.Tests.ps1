# Windows betiğinin yedek → uygula → geri yükle döngüsünü GERÇEK SİSTEME DOKUNMADAN test eder.
# Tablo yalnızca HKCU:\Software\UMToptimizerTest altındaki sahte anahtarlarla değiştirilir, veri klasörü geçicidir.
# Çalıştırma: powershell -NoProfile -ExecutionPolicy Bypass -File tests\WindowsScript.Tests.ps1
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '..\src\UMToptimizer\Resources\Windows\UMToptimizer-Windows.ps1'
$tmp = Join-Path $env:TEMP ("umt-pstest-" + [guid]::NewGuid().ToString('N'))
$K = 'HKCU:\Software\UMToptimizerTest'
$fail = 0
function Check($name, $cond) { if ($cond) { Write-Host "  ✓ $name" -ForegroundColor Green } else { Write-Host "  ✗ $name" -ForegroundColor Red; $script:fail++ } }

try {
    # Nokta ile yükle: yalnızca fonksiyonlar tanımlanır (giriş noktası çalışmaz)
    . $script -DataRoot $tmp
    $ErrorActionPreference = 'Stop'
    New-Item -ItemType Directory -Force -Path $tmp, (Join-Path $tmp 'logs') | Out-Null
    $LogPath = Join-Path $tmp 'logs\test.log'

    # Sahte başlangıç durumu: biri var (DWord), biri var (Binary), biri yok, anahtarı hiç olmayan biri
    Remove-Item $K -Recurse -Force -ErrorAction SilentlyContinue
    New-Item $K -Force | Out-Null
    New-ItemProperty $K -Name Existing -Value 7 -PropertyType DWord | Out-Null
    New-ItemProperty $K -Name Bin -Value ([byte[]](1, 2, 3)) -PropertyType Binary | Out-Null
    $Registry = @(
        , @('S', $K, 'Existing', 0)
        , @('S', $K, 'Bin', [byte[]](9, 9))
        , @('S', $K, 'Missing', 'yeni')
        , @('A', "$K\NewKey", 'Val', 1)
    )
    $Services = @{ S = [ordered]@{}; A = [ordered]@{} }

    Write-Host '1) Yedek'
    Save-Backup
    $b = Get-Content $Backup -Raw | ConvertFrom-Json
    Check 'backup.json oluştu' (Test-Path $Backup)
    Check 'mevcut değer yedeklendi (7)' (($b.Registry | Where-Object Name -EQ Existing).Value -eq 7)
    Check 'olmayan değer Exists=false' (-not ($b.Registry | Where-Object Name -EQ Missing).Exists)
    Check 'olmayan anahtar KeyExisted=false' (-not ($b.Registry | Where-Object Name -EQ Val).KeyExisted)
    Check 'IPv6 bağlamaları yedeğe yazıldı (eski hata)' ($null -ne $b.PSObject.Properties['IPv6'])

    Write-Host '2) Uygula (tablodaki değerler)'
    foreach ($r in $Registry) { Set-Reg $r[1] $r[2] $r[3] }
    Check 'Existing=0' ((Get-ItemProperty $K).Existing -eq 0)
    Check 'Missing oluştu' ((Get-ItemProperty $K).Missing -eq 'yeni')
    Check 'NewKey oluştu' (Test-Path "$K\NewKey")

    Write-Host '3) İkinci uygulama yedeği EZMEZ'
    Save-Backup
    Check 'yedekte hâlâ orijinal 7 var' (((Get-Content $Backup -Raw | ConvertFrom-Json).Registry | Where-Object Name -EQ Existing).Value -eq 7)

    Write-Host '4) Güvenlik: kurcalanmış yedek (saldırgan girdisi eklenir)'
    $evil = Get-Content $Backup -Raw | ConvertFrom-Json
    $evil.Registry += [pscustomobject]@{ Path = "$K\Evil"; Name = 'ImagePath'; KeyExisted = $true; Exists = $true; Kind = 'String'; Value = 'C:\kotu.exe' }
    $evil.Services += [pscustomobject]@{ Name = 'Spooler'; StartMode = 'Disabled'; Delayed = $false }
    $evil | ConvertTo-Json -Depth 6 | Set-Content $Backup -Encoding UTF8

    Write-Host '5) Geri yükle'
    $ErrorActionPreference = 'Continue'
    Restore-All 6>$null
    $p = Get-ItemProperty $K
    Check 'Existing eski değerine döndü (7)' ($p.Existing -eq 7)
    Check 'Binary eski değerine döndü (1,2,3)' ((@($p.Bin) -join ',') -eq '1,2,3')
    Check 'önceden olmayan değer silindi' ($null -eq $p.PSObject.Properties['Missing'])
    Check 'bizim oluşturduğumuz boş anahtar silindi' (-not (Test-Path "$K\NewKey"))
    Check 'tablo dışı (saldırgan) anahtar YAZILMADI' (-not (Test-Path "$K\Evil"))
    Check 'yedek arşivlendi' ((-not (Test-Path $Backup)) -and [bool](Get-ChildItem $tmp -Filter 'backup-restored-*.json'))
    Check 'günlükte atlama kaydı var' ((Get-Content $LogPath -Raw) -match 'izin verilmeyen girdi atlandı')

    Write-Host '6) Hosts filtresi'
    $lines = Select-BlockLines "# yorum`n0.0.0.0 adobe.com`n127.0.0.1 a.b.c`n1.2.3.4 banka.com`n0.0.0.0 x.com; rm`n0.0.0.0 adobe.com"
    Check 'yalnızca 2 geçerli engelleme satırı' ($lines.Count -eq 2)
    Check 'yönlendirme satırı reddedildi' (-not ($lines -match 'banka'))

    Write-Host '7) LogPath doğrulaması'
    $r = & powershell.exe -NoProfile -Command ". '$script' -DataRoot '$tmp' -LogPath 'C:\Windows\evil.log'; `$LogPath"
    Check 'veri klasörü dışındaki günlük yolu reddedildi' ($r -eq "$tmp\logs\windows.log")
    $r = & powershell.exe -NoProfile -Command ". '$script' -DataRoot '$tmp' -LogPath '$tmp\logs\..\..\x.log'; `$LogPath"
    Check '.. ile dışarı çıkma reddedildi' ($r -eq "$tmp\logs\windows.log")

    Write-Host '8) HKCU yönlendirme (başka kullanıcı SID''i)'
    Check 'kendi oturumumuzda HKCU: kalır' ((Resolve-Key 'HKCU:\Software\X') -eq 'HKCU:\Software\X')
    $Hkcu = 'Registry::HKEY_USERS\S-1-5-21-1-2-3-1001'; $HkcuClasses = 'Registry::HKEY_USERS\S-1-5-21-1-2-3-1001_Classes'
    Check 'HKCU → HKEY_USERS\<sid>' ((Resolve-Key 'HKCU:\Control Panel\Desktop') -eq 'Registry::HKEY_USERS\S-1-5-21-1-2-3-1001\Control Panel\Desktop')
    Check 'Classes → <sid>_Classes' ((Resolve-Key 'HKCU:\Software\Classes\CLSID\{x}') -eq 'Registry::HKEY_USERS\S-1-5-21-1-2-3-1001_Classes\CLSID\{x}')
    Check 'HKLM değişmez' ((Resolve-Key 'HKLM:\SOFTWARE\X') -eq 'HKLM:\SOFTWARE\X')
}
finally {
    Remove-Item $K -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host ("Sonuç: {0}" -f $(if ($fail) { "$fail BAŞARISIZ" } else { 'hepsi geçti' }))
exit $fail
