# Windows betiğinin yedek → uygula → geri yükle döngüsünü ve güvenlik denetimlerini GERÇEK SİSTEME DOKUNMADAN test eder.
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
    Check 'IPv6 bağlamaları yedeğe yazıldı' ($null -ne $b.PSObject.Properties['IPv6'])

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
    $lines = Select-BlockLines "# yorum`n0.0.0.0 adobe.com`n127.0.0.1 lm.licenses.adobe.com`n1.2.3.4 banka.com`n0.0.0.0 adobe.com; rm`n0.0.0.0 adobe.com`n0.0.0.0 github.com`n0.0.0.0 cc-api-data.adobe.io"
    Check 'yalnızca 3 geçerli engelleme satırı (tekrar ayıklandı)' ($lines.Count -eq 3)
    Check 'yönlendirme satırı (1.2.3.4) reddedildi' (-not ($lines -match 'banka'))
    Check 'Adobe dışı alan adı (github.com) reddedildi' (-not ($lines -match 'github'))
    Check 'komut eklenmiş satır reddedildi' (-not ($lines -match 'rm'))

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

    Write-Host '9) Güvenlik: Remove-Links bağlantıları takip etmez, yalnızca bağlantıyı kaldırır'
    $victim = Join-Path $tmp 'kurban'; $tree = Join-Path $tmp 'veri-benzeri'
    New-Item -ItemType Directory -Force -Path $victim, "$tree\alt" | Out-Null
    Set-Content "$victim\onemli.txt" 'silinmemeli'
    Set-Content "$tree\alt\backup.json" '{}'
    cmd.exe /c mklink /J "$tree\baglanti" "$victim" | Out-Null
    cmd.exe /c mklink /J "$tree\alt\baglanti2" "$victim" | Out-Null
    Check 'junction kuruldu (ön koşul)' (Test-Link "$tree\baglanti")
    Remove-Links $tree
    Check 'junction hedefindeki dosya DURUYOR' (Test-Path "$victim\onemli.txt")
    Check 'üst düzey bağlantı kaldırıldı' (-not (Test-Path "$tree\baglanti"))
    Check 'alt klasördeki bağlantı kaldırıldı' (-not (Test-Path "$tree\alt\baglanti2"))
    Check 'normal dosyaya dokunulmadı' (Test-Path "$tree\alt\backup.json")
    Check 'betikte özyinelemeli silme fonksiyonu kalmadı' (-not (Get-Command Clear-Tree -ErrorAction SilentlyContinue))

    Write-Host '10) Güvenlik: DACL tamamen değiştirilir (önceden eklenmiş açık kural kalmaz)'
    $acl = Join-Path $tmp 'dacl'
    New-Item -ItemType Directory -Force -Path $acl | Out-Null
    Check 'başlangıçta güvenilir (yalnızca bize ve sisteme ait kurallar)' (Test-TrustedAcl $acl)
    icacls.exe $acl /grant '*S-1-1-0:(OI)(CI)M' /Q | Out-Null     # saldırganın önceden eklediği kural: Everyone → Değiştir
    Check 'yabancı yazma kuralı güvenilmez sayılır' (-not (Test-TrustedAcl $acl))
    Set-ProtectedAcl $acl
    Check 'Set-ProtectedAcl sonrası güvenilir' (Test-TrustedAcl $acl)
    $sddl = (Get-Acl -LiteralPath $acl).Sddl
    Check 'Everyone kuralı kaldırıldı' ($sddl -notmatch ';;;WD\)')
    Check 'kalıtım kapatıldı (korumalı DACL)' ($sddl -match 'D:PAI')
    Check 'olmayan yol güvenilir sayılmaz' (-not (Test-TrustedAcl "$tmp\yok-boyle-bir-sey"))

    Write-Host '11) Yedek atomik yazılır; yazılamazsa mevcut yedek bozulmaz'
    Write-Backup ([ordered]@{ Registry = @(1); Aggressive = $false })
    Check 'yedek yazıldı, geçici dosya kalmadı' ((Test-Path $Backup) -and -not (Test-Path "$Backup.tmp"))
    $threw = $false; try { Write-Backup ([ordered]@{ Baska = 1 }) } catch { $threw = $true }
    Check 'geçersiz yedek reddedildi (hata fırlatıldı)' $threw
    Check 'mevcut yedek olduğu gibi duruyor' ((Get-Content $Backup -Raw | ConvertFrom-Json).Registry[0] -eq 1)

    Write-Host '12) Bozuk mevcut yedek "orijinal" sayılmaz'
    Set-Content -LiteralPath $Backup -Value '{ yarım kalmış' -Encoding UTF8
    Save-Backup
    Check 'bozuk yedek ayrı dosyaya taşındı' ([bool](Get-ChildItem $tmp -Filter 'backup-corrupt-*.json'))
    Check 'yeni yedek okunabiliyor' (@((Get-Content $Backup -Raw | ConvertFrom-Json).Registry).Count -eq 4)

    Write-Host '13) Sahiplik denetimi'
    Check 'kendi dosyamız güvenilir sayılır' (Test-TrustedOwner $Backup)
    Check 'olmayan yol güvenilir sayılmaz' (-not (Test-TrustedOwner "$tmp\yok-boyle-bir-sey"))

    Write-Host '14) Dış programlar süre sınırıyla çalışır (takılan adım profili kilitlemez)'
    $ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Check 'biten programın çıkış kodu döner' ((Invoke-Timed $ps '-NoProfile -Command exit 7' 60) -eq 7)
    # OneDrive/Edge kaldırıcıları gibi arka planda süreç bırakıp çıkan program: bırakılan süreç beklenmez
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-Timed $ps '-NoProfile -Command "Start-Process ping.exe ''-n 30 127.0.0.1'' -WindowStyle Hidden; exit 0"' 60
    Check 'arka planda süreç bırakan program beklenmez' ($code -eq 0 -and $sw.Elapsed.TotalSeconds -lt 20)
    $sw.Restart(); $threw = $false
    try { Invoke-Timed $ps '-NoProfile -Command Start-Sleep 120' 3 | Out-Null } catch { $threw = $true }
    Check 'takılan program süre dolunca durdurulur ve hata bildirilir' ($threw -and $sw.Elapsed.TotalSeconds -lt 30)
    Step 'takılan adım' { Invoke-Timed $ps '-NoProfile -Command Start-Sleep 120' 2 } 6>$null
    Check 'takılan adım günlüğe ✗ olarak yazılır, betik devam eder' ((Get-Content $LogPath -Raw) -match '✗ takılan adım')
}
finally {
    Remove-Item $K -Recurse -Force -ErrorAction SilentlyContinue
    icacls.exe $tmp /reset /T /C /Q 2>$null | Out-Null
    if (Get-Command Remove-Links -ErrorAction SilentlyContinue) { Remove-Links $tmp }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host ("Sonuç: {0}" -f $(if ($fail) { "$fail BAŞARISIZ" } else { 'hepsi geçti' }))
exit $fail
