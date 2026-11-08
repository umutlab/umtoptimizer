# UMToptimizer 3 — Windows profilleri (Windows 10/11, PowerShell 5.1+)
#
# Tasarım: her kayıt defteri ayarı ve hizmet aşağıdaki tablolarda TEK KEZ tanımlanır.
# Uygulama, yedekleme ve geri yükleme aynı tablodan türetilir; böylece yedek listesi
# ile uygulanan ayarlar asla birbirinden kopamaz (eski sürümde elle tutulan ayrı bir
# yedek listesi vardı; bazı anahtarlar yanlış yolda, bazıları eksikti).
#
# Uygulamadan:   powershell -File UMToptimizer-Windows.ps1 -Mode Standard -Yes [-RestorePoint] [-AllowBitLocker]
# Elle (menü):   Yönetici PowerShell'de .\UMToptimizer-Windows.ps1
[CmdletBinding()]
param(
    [ValidateSet('Standard', 'Aggressive', 'Restore')][string]$Mode,
    [switch]$Yes,                # onayları atla (uygulama arayüzü zaten onay aldı)
    [switch]$RestorePoint,       # önce Sistem Geri Yükleme noktası oluştur
    [switch]$AllowBitLocker,     # BitLocker açıksa kapatmaya izin ver
    [switch]$ReenableBitLocker,  # geri yüklemede BitLocker'ı yeniden aç
    [string]$LogPath,            # yalnızca %ProgramData%\UMToptimizer\logs altı kabul edilir
    [string]$UserSid,            # uygulamayı açan kullanıcı (yükseltme başka hesapla yapıldıysa HKCU onun için ayarlanır)
    [string]$DataRoot = (Join-Path $env:ProgramData 'UMToptimizer')  # testler için değiştirilebilir
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$Data = $DataRoot
$Backup = Join-Path $Data 'backup.json'
$Logs = Join-Path $Data 'logs'
$HostsFile = Join-Path $env:WINDIR 'System32\drivers\etc\hosts'
$HostsBegin = '# >>> UMToptimizer Adobe block list'
$HostsEnd = '# <<< UMToptimizer'
$ValidKinds = 'String', 'ExpandString', 'Binary', 'DWord', 'QWord', 'MultiString'

# GÜVENLİK: günlük yolu dışarıdan gelir; veri klasörü dışına yazmaya izin verme.
$full = if ($LogPath) { [IO.Path]::GetFullPath($LogPath) } else { '' }
if (-not $full.StartsWith($Logs + '\', [StringComparison]::OrdinalIgnoreCase) -or $full.Contains('..')) { $LogPath = Join-Path $Logs 'windows.log' }

function Log([string]$Message) {
    $line = '[{0:HH:mm:ss}] {1}' -f (Get-Date), $Message
    Write-Host $line
    try { Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 -ErrorAction Stop } catch { }
}

# GÜVENLİK: %ProgramData% altında standart kullanıcılar klasör oluşturup SAHİBİ olabilir. Biri bu klasörü
# önceden oluşturup sahte backup.json ya da junction/symlink bırakırsa, yönetici olarak çalışan bu betik
# saldırganın seçtiği kayıt defteri değerlerini veya dosya yollarını yazar (yerel yetki yükseltme).
# Bu yüzden her çalıştırmada: bağlantılar silinir, sahip Administrators yapılır, yazma yalnızca SYSTEM/Administrators'a verilir.
function Protect-DataDir {
    foreach ($d in $Data, $Logs) {
        $item = Get-Item -LiteralPath $d -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { $item.Delete() }
        New-Item -ItemType Directory -Force -Path $d | Out-Null
    }
    Get-ChildItem -LiteralPath $Data -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | ForEach-Object { $_.Delete() }
    icacls.exe $Data /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
    icacls.exe $Data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /C /Q | Out-Null
    icacls.exe "$Data\*" /reset /T /C /Q 2>$null | Out-Null
}

# Yükseltme başka bir yönetici hesabıyla yapıldıysa (standart kullanıcı + UAC parolası), HKCU/TEMP
# yönetici hesabını gösterir. Uygulama kendi kullanıcısının SID'ini verir; kullanıcı ayarları ONA yazılır.
$Hkcu = 'HKCU:'; $HkcuClasses = 'HKCU:\Software\Classes'; $UserLocal = $env:LOCALAPPDATA; $UserTemp = $env:TEMP
if ($UserSid -match '^S-1-5-21(-\d+){1,8}$' -and $UserSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -and
    (Test-Path "Registry::HKEY_USERS\$UserSid")) {
    $Hkcu = "Registry::HKEY_USERS\$UserSid"
    $HkcuClasses = "Registry::HKEY_USERS\${UserSid}_Classes"
    $profileDir = (Get-CimInstance Win32_UserProfile -Filter "SID='$UserSid'" -ErrorAction SilentlyContinue).LocalPath
    if ($profileDir) { $UserLocal = Join-Path $profileDir 'AppData\Local'; $UserTemp = Join-Path $UserLocal 'Temp' }
}
function Resolve-Key([string]$Path) {
    if ($Path -like 'HKCU:\Software\Classes*') { return $HkcuClasses + $Path.Substring(22) }
    if ($Path -like 'HKCU:*') { return $Hkcu + $Path.Substring(5) }
    $Path
}

# Bir adımı çalıştırır, sonucu tek satırda günlüğe yazar; hata tüm profili durdurmaz.
function Step([string]$What, [scriptblock]$Do) {
    try { & $Do | Out-Null; Log "✓ $What" } catch { Log "✗ ${What}: $($_.Exception.Message)" }
}

# ============================== AYAR TABLOLARI ==============================
# Sütunlar: Seviye (S=Standart, A=Agresif) | Yol | Ad | Değer
# Tür değerden çıkarılır: [int] → DWord, [string] → String, [byte[]] → Binary
$P = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows'
$LM = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion'
$CU = 'HKCU:\Software\Microsoft\Windows\CurrentVersion'
$Adv = "$CU\Explorer\Advanced"
$Desk = 'HKCU:\Control Panel\Desktop'
$Edge = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
$Brave = 'HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'

$Registry = @(
    # --- Standart: gizlilik, telemetri, konum, tüketici özellikleri
    , @('S', "$P\System", 'EnableActivityFeed', 0)
    , @('S', "$P\System", 'PublishUserActivities', 0)
    , @('S', "$P\System", 'UploadUserActivities', 0)
    , @('S', 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager', 'DisableWpbtExecution', 1)
    , @('S', "$P\CloudContent", 'DisableWindowsConsumerFeatures', 1)
    , @('S', "$LM\CapabilityAccessManager\ConsentStore\location", 'Value', 'Deny')
    , @('S', 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}', 'SensorPermissionState', 0)
    , @('S', 'HKLM:\SYSTEM\Maps', 'AutoUpdateEnabled', 0)
    , @('S', "$Adv\TaskbarDeveloperSettings", 'TaskbarEndTask', 1)
    , @('S', "$P\DataCollection", 'AllowTelemetry', 0)
    , @('S', "$LM\Policies\DataCollection", 'AllowTelemetry', 0)
    , @('S', $Adv, 'Start_TrackProgs', 0)
    , @('S', "$P\DeliveryOptimization", 'DODownloadMode', 0)
    , @('S', "$P\Device Metadata", 'PreventDeviceMetadataFromNetwork', 1)
    , @('S', 'HKLM:\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\3036241548', 'EnabledState', 1)
    , @('S', "$LM\Explorer\FlyoutMenuSettings", 'ShowHibernateOption', 0)
    , @('S', 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power', 'HibernateEnabled', 0)
    , @('S', 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore', 'SystemRestorePointCreationFrequency', 0)
    , @('S', 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell', 'FolderType', 'NotSpecified')
    # --- Agresif: arama, bildirimler, arayüz, ağ, AI, sürücü yardımcıları
    , @('A', "$P\Windows Search", 'AllowSearchToUseLocation', 0)
    , @('A', "$P\Windows Search", 'DisableWebSearch', 1)
    , @('A', "$P\Explorer", 'DisableNotificationCenter', 1)
    , @('A', 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh', 'AllowNewsAndInterests', 0)
    , @('A', "$P\WindowsCopilot", 'TurnOffWindowsCopilot', 1)
    , @('A', "$CU\BackgroundAccessApplications", 'GlobalUserDisabled', 1)
    , @('A', 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters', 'DisabledComponents', 255)
    , @('A', "$CU\StorageSense\Parameters\StoragePolicy", '01', 0)
    , @('A', 'HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'DisableNotificationCenter', 1)
    , @('A', "$CU\PushNotifications", 'ToastEnabled', 0)
    , @('A', 'HKCU:\Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}', 'System.IsPinnedToNameSpaceTree', 0)
    , @('A', 'HKCU:\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}', 'System.IsPinnedToNameSpaceTree', 0)
    , @('A', $Adv, 'LaunchTo', 1)
    , @('A', $Adv, 'TaskbarAnimations', 0)
    , @('A', $Adv, 'ListviewShadow', 0)
    , @('A', $Adv, 'ListviewAlphaSelect', 0)
    , @('A', $Adv, 'TaskbarMn', 0)
    , @('A', 'HKCU:\Software\Microsoft\Windows\DWM', 'EnableAeroPeek', 0)
    , @('A', "$CU\Explorer\VisualEffects", 'VisualFXSetting', 3)
    , @('A', $Desk, 'DragFullWindows', '0')
    , @('A', $Desk, 'MenuShowDelay', '200')
    , @('A', $Desk, 'UserPreferencesMask', [byte[]](144, 18, 3, 128, 16, 0, 0, 0))
    , @('A', "$Desk\WindowMetrics", 'MinAnimate', '0')
    , @('A', 'HKCU:\Control Panel\Keyboard', 'KeyboardDelay', '0')
    , @('A', 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32', '(default)', '')  # klasik sağ tık menüsü
    , @('A', "$Edge\ExtensionInstallBlocklist", '1', 'ofefcgjbeghpigppfmkologfjadafddi')
    , @('A', 'HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate', 'CreateDesktopShortcutDefault', 0)
    , @('A', "$LM\Policies\Explorer", 'SettingsPageVisibility', 'hide:aicomponents')
    , @('A', 'HKLM:\SOFTWARE\Policies\WindowsNotepad', 'DisableAIFeatures', 1)
    , @('A', "$LM\DriverSearching", 'SearchOrderConfig', 0)
    , @('A', "$LM\Device Installer", 'DisableCoInstallers', 1)
)
# Tarayıcı politikaları: tanıtım, toplama ve telemetri kapalı
$Registry += 'PersonalizationReportingEnabled', 'ShowRecommendationsEnabled', 'UserFeedbackAllowed', 'AlternateErrorPagesEnabled',
    'EdgeCollectionsEnabled', 'EdgeShoppingAssistantEnabled', 'MicrosoftEdgeInsiderPromotionEnabled', 'ShowMicrosoftRewards',
    'WebWidgetAllowed', 'DiagnosticData', 'EdgeAssetDeliveryServiceEnabled', 'WalletDonationEnabled', 'DefaultBrowserSettingsCampaignEnabled' |
    ForEach-Object { , @('A', $Edge, $_, 0) }
$Registry += @(@('A', $Edge, 'HideFirstRunExperience', 1), @('A', $Edge, 'ConfigureDoNotTrack', 1))
$Registry += 'BraveRewardsDisabled', 'BraveWalletDisabled', 'BraveVPNDisabled', 'BraveNewsDisabled', 'BraveTalkDisabled', 'TorDisabled' |
    ForEach-Object { , @('A', $Brave, $_, 1) }
$Registry += 'BraveAIChatEnabled', 'BraveStatsPingEnabled', 'BraveP3AEnabled', 'UrlKeyedAnonymizedDataCollectionEnabled',
    'SafeBrowsingExtendedReportingEnabled', 'MetricsReportingEnabled' | ForEach-Object { , @('A', $Brave, $_, 0) }

$Services = @{
    S = [ordered]@{ CscService = 'disabled'; DiagTrack = 'disabled'; MapsBroker = 'demand'; StorSvc = 'demand'; SharedAccess = 'disabled'; WerSvc = 'disabled'; lfsvc = 'disabled' }
    A = [ordered]@{ RetailDemo = 'disabled'; SysMain = 'disabled'; WSearch = 'disabled'; DoSvc = 'demand'; OneSyncSvc = 'disabled'; WSAIFabricSvc = 'disabled' }
}

$OptionalApps = 'Microsoft.BingNews', 'Microsoft.BingWeather', 'Microsoft.GetHelp', 'Microsoft.Getstarted', 'Microsoft.MicrosoftOfficeHub',
    'Microsoft.MicrosoftSolitaireCollection', 'Microsoft.People', 'Microsoft.Todos', 'Microsoft.WindowsFeedbackHub', 'Microsoft.WindowsMaps',
    'Microsoft.YourPhone', 'Microsoft.ZuneMusic', 'Microsoft.ZuneVideo', 'MicrosoftCorporationII.QuickAssist', 'Microsoft.Windows.DevHome',
    'Microsoft.Copilot', 'MicrosoftWindows.Client.CoreAI'

# ============================== YARDIMCILAR ==============================
function Set-Reg($Path, $Name, $Value, $Kind) {
    if (-not $Kind) { $Kind = if ($Value -is [byte[]]) { 'Binary' } elseif ($Value -is [int]) { 'DWord' } else { 'String' } }
    $Path = Resolve-Key $Path
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType $Kind -Force | Out-Null
}

function Read-Reg($Path, $Name) {
    $key = Get-Item -LiteralPath (Resolve-Key $Path) -ErrorAction SilentlyContinue
    $n = if ($Name -eq '(default)') { '' } else { $Name }
    $item = [ordered]@{ Path = $Path; Name = $Name; KeyExisted = [bool]$key; Exists = $false }
    if ($key -and ($key.GetValueNames() -contains $n)) {
        $item.Exists = $true
        $item.Kind = $key.GetValueKind($n).ToString()
        $item.Value = $key.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
    }
    $item
}

function Set-ServiceStart($Name, $Start) {
    if (-not (Get-Service -Name $Name -ErrorAction SilentlyContinue)) { return }
    $out = & sc.exe config $Name start= $Start 2>&1
    if ($LASTEXITCODE -eq 0) { Log "✓ Hizmet $Name → $Start" } else { Log "✗ Hizmet ${Name}: $out" }
}

function Remove-Apps([string[]]$Names) {
    $provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue)
    foreach ($n in $Names) {
        foreach ($pkg in @(Get-AppxPackage -AllUsers -Name $n -ErrorAction SilentlyContinue)) {
            Step "Uygulama kaldırıldı: $($pkg.Name)" { Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction Stop }
        }
        foreach ($pkg in $provisioned | Where-Object DisplayName -EQ $n) {
            Step "Hazır paket kaldırıldı: $n" { Remove-AppxProvisionedPackage -Online -PackageName $pkg.PackageName -ErrorAction Stop }
        }
    }
}

function Test-BitLockerOn {
    try { $v = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; $v.ProtectionStatus -eq 'On' -or $v.VolumeStatus -eq 'FullyEncrypted' } catch { $false }
}

function Test-HostsBlock { (Get-Content -LiteralPath $HostsFile -Raw -ErrorAction SilentlyContinue) -match [regex]::Escape($HostsBegin) }

# GÜVENLİK: üçüncü taraf listeden yalnızca "0.0.0.0 alan.adi" / "127.0.0.1 alan.adi" engelleme satırları kabul edilir.
# Liste ele geçirilse bile hosts'a "1.2.3.4 banka.com" gibi yönlendirme satırı giremez.
function Select-BlockLines([string]$Text) {
    @($Text -split '\r?\n' | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^(0\.0\.0\.0|127\.0\.0\.1)\s+[A-Za-z0-9][A-Za-z0-9.-]{0,252}$' } | Select-Object -Unique)
}

# ============================== YEDEK / GERİ YÜKLEME ==============================
function Save-Backup {
    if (Test-Path -LiteralPath $Backup) { Log 'Önceki yedek korunuyor (ilk uygulamadan önceki orijinal değerler).'; return }
    $names = @($Services.S.Keys) + @($Services.A.Keys)
    $reserved = try { "$((Get-WindowsReservedStorageState -ErrorAction Stop).ReservedStorageState)" } catch { '' }
    $defender = try { (Get-MpPreference -ErrorAction Stop).SubmitSamplesConsent } catch { $null }
    # DİKKAT: PowerShell değişken adları büyük/küçük harf duyarsızdır; "$backup" yazmak $Backup (dosya yolu) değişkenini ezerdi.
    $snapshot = [ordered]@{
        Created         = (Get-Date).ToString('o')
        Registry        = @($Registry | ForEach-Object { Read-Reg $_[1] $_[2] })
        Services        = @(foreach ($n in $names) {
                $s = Get-CimInstance Win32_Service -Filter "Name='$n'" -ErrorAction SilentlyContinue
                if ($s) { [ordered]@{ Name = $n; StartMode = $s.StartMode; Delayed = [bool]$s.DelayedAutoStart } }
            })
        IPv6            = @(Get-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue | ForEach-Object { [ordered]@{ Name = $_.Name; Enabled = $_.Enabled } })
        ReservedStorage = $reserved
        Defender        = $defender
        BitLockerWasOn  = Test-BitLockerOn
        Aggressive      = $false
    }
    $snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Backup -Encoding UTF8
    Log "✓ Yedek kaydedildi: $Backup ($($snapshot.Registry.Count) kayıt defteri değeri, $($snapshot.Services.Count) hizmet)"
}

function Restore-All {
    if (-not (Test-Path -LiteralPath $Backup)) { throw "Yedek bulunamadı: $Backup" }
    $b = Get-Content -LiteralPath $Backup -Raw | ConvertFrom-Json

    # GÜVENLİK: yedek dosyası yalnızca tablodaki anahtar/hizmetleri geri yükleyebilir (beyaz liste).
    # Dosya kurcalanmış olsa bile rastgele HKLM değeri (ör. hizmet ImagePath) yazılamaz.
    $allowedReg = @{}; foreach ($t in $Registry) { $allowedReg["$($t[1])|$($t[2])"] = $true }
    $allowedSvc = @($Services.S.Keys) + @($Services.A.Keys)

    $done = 0
    foreach ($r in $b.Registry) {
        if (-not $allowedReg["$($r.Path)|$($r.Name)"] -or ($r.Exists -and $ValidKinds -notcontains $r.Kind)) { Log "✗ Yedekte izin verilmeyen girdi atlandı: $($r.Path)\$($r.Name)"; continue }
        try {
            $path = Resolve-Key $r.Path
            if ($r.Exists) {
                $v = switch ($r.Kind) { 'Binary' { [byte[]]$r.Value } 'DWord' { [int]$r.Value } 'QWord' { [long]$r.Value } 'MultiString' { [string[]]$r.Value } default { [string]$r.Value } }
                Set-Reg $r.Path $r.Name $v $r.Kind
            } elseif (Test-Path -LiteralPath $path) {
                Remove-ItemProperty -LiteralPath $path -Name $r.Name -ErrorAction SilentlyContinue
                # Anahtarı biz oluşturduysak ve boş kaldıysa onu da temizle
                $k = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
                if (-not $r.KeyExisted -and $k -and $k.ValueCount -eq 0 -and $k.SubKeyCount -eq 0) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
            }
            $done++
        } catch { Log "✗ $($r.Path)\$($r.Name): $($_.Exception.Message)" }
    }
    Log "✓ $done kayıt defteri değeri geri yüklendi"

    foreach ($s in $b.Services) {
        if ($allowedSvc -notcontains $s.Name) { continue }
        $start = switch ($s.StartMode) { 'Auto' { if ($s.Delayed) { 'delayed-auto' } else { 'auto' } } 'Manual' { 'demand' } 'Disabled' { 'disabled' } default { $null } }
        if ($start) { Set-ServiceStart $s.Name $start }
    }

    $h = $b.Registry | Where-Object { $_.Name -eq 'HibernateEnabled' } | Select-Object -First 1
    if ($h -and $h.Exists) { $state = if ($h.Value -eq 1) { 'on' } else { 'off' }; Step "Hazırda bekletme: $state" { powercfg.exe /hibernate $state } }

    foreach ($i in $b.IPv6) {
        Step "IPv6 bağlaması geri yüklendi: $($i.Name)" {
            if ($i.Enabled) { Enable-NetAdapterBinding -Name $i.Name -ComponentID ms_tcpip6 -ErrorAction Stop }
            else { Disable-NetAdapterBinding -Name $i.Name -ComponentID ms_tcpip6 -ErrorAction Stop }
        }
    }
    if ($b.Aggressive) { Step 'Teredo varsayılana döndü' { netsh.exe interface teredo set state default } }
    if ($b.ReservedStorage -eq 'Enabled') { Step 'Ayrılmış depolama açıldı' { Set-WindowsReservedStorageState -State Enabled -ErrorAction Stop } }
    if ("$($b.Defender)" -match '^[0-3]$') { Step 'Defender örnek gönderimi geri yüklendi' { Set-MpPreference -SubmitSamplesConsent ([int]$b.Defender) -ErrorAction Stop } }

    if (Test-HostsBlock) {
        Step 'Hosts dosyasındaki UMToptimizer bloğu kaldırıldı' {
            $text = Get-Content -LiteralPath $HostsFile -Raw
            $clean = [regex]::Replace($text, "(?s)\s*$([regex]::Escape($HostsBegin)).*?$([regex]::Escape($HostsEnd))", '')
            # Default = sistem ANSI kod sayfası; dosyadaki Türkçe yorumlar bozulmaz (ASCII '?' yapıyordu)
            Set-Content -LiteralPath $HostsFile -Value $clean.TrimEnd() -Encoding Default
            ipconfig.exe /flushdns
        }
    }
    if ($ReenableBitLocker -and $b.BitLockerWasOn) {
        Step 'BitLocker yeniden etkinleştirme istendi' { Enable-BitLocker -MountPoint $env:SystemDrive -RecoveryPasswordProtector -ErrorAction Stop }
    }

    # Yedeği arşivle: bir sonraki uygulama güncel durumdan yeni bir yedek alır
    Move-Item -LiteralPath $Backup -Destination (Join-Path $Data ('backup-restored-{0:yyyyMMdd-HHmmss}.json' -f (Get-Date))) -Force
    Log "Geri yükleme tamamlandı. Windows'u yeniden başlatın."
}

# ============================== PROFİL ==============================
function Apply-Profile([bool]$Aggressive) {
    $levels = if ($Aggressive) { 'S', 'A' } else { , 'S' }

    # Klasör görünümü önbelleğini sıfırla (FolderType ayarından ÖNCE olmalı; bu adım geri alınamaz)
    Remove-Item (Resolve-Key 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags'), (Resolve-Key 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU') -Recurse -Force -ErrorAction SilentlyContinue

    $set = 0
    foreach ($r in $Registry | Where-Object { $levels -contains $_[0] }) {
        try { Set-Reg $r[1] $r[2] $r[3]; $set++ } catch { Log "✗ $($r[1])\$($r[2]): $($_.Exception.Message)" }
    }
    Log "✓ $set kayıt defteri ayarı uygulandı"
    foreach ($l in $levels) { foreach ($s in $Services[$l].GetEnumerator()) { Set-ServiceStart $s.Key $s.Value } }

    Step 'Hazırda bekletme kapatıldı' { powercfg.exe /hibernate off }
    Step 'Defender otomatik örnek gönderimi kapatıldı' { Set-MpPreference -SubmitSamplesConsent 2 -ErrorAction Stop }
    $storeDb = Join-Path $UserLocal 'Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db'
    if (Test-Path -LiteralPath $storeDb) { Step 'Mağaza önerilen arama veritabanı engellendi' { icacls.exe $storeDb /deny '*S-1-1-0:F' } }
    if (Test-BitLockerOn) {
        if ($AllowBitLocker) { Step 'BitLocker şifre çözme başlatıldı' { Disable-BitLocker -MountPoint $env:SystemDrive -ErrorAction Stop } }
        else { Log 'BitLocker değiştirilmedi (izin verilmedi).' }
    }
    Remove-Apps 'Microsoft.WidgetsPlatformRuntime', 'MicrosoftWindows.Client.WebExperience'

    if ($Aggressive) {
        Step 'IPv6 bağdaştırıcı bağlamaları kapatıldı' { Get-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction Stop | Disable-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction Stop }
        Step 'Teredo kapatıldı' { netsh.exe interface teredo set state disabled }
        Step 'Ayrılmış depolama kapatıldı' { Set-WindowsReservedStorageState -State Disabled -ErrorAction Stop }
        Step 'Recall kapatıldı' { Disable-WindowsOptionalFeature -FeatureName Recall -Online -NoRestart -ErrorAction Stop }
        Remove-Apps $OptionalApps
        $oneDrive = Join-Path $env:WINDIR 'System32\OneDriveSetup.exe'
        if (Test-Path $oneDrive) { Step 'OneDrive kaldırıldı' { Start-Process $oneDrive '/uninstall' -Wait } }
        $edgeSetup = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\*\Installer\setup.exe') -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($edgeSetup) { Step 'Edge kaldırma başlatıldı' { Start-Process $edgeSetup.FullName '--uninstall --system-level --force-uninstall --delete-profile' -Wait } }
        Stop-Process -Name 'logi_download_assistant' -Force -ErrorAction SilentlyContinue
        if (-not (Test-HostsBlock)) {
            Step 'Adobe engelleme listesi hosts dosyasına eklendi' {
                $list = Select-BlockLines (Invoke-RestMethod 'https://github.com/Ruddernation-Designs/Adobe-URL-Block-List/raw/refs/heads/master/hosts' -TimeoutSec 20)
                if ($list.Count -eq 0) { throw 'Listede geçerli engelleme satırı yok.' }
                Add-Content -LiteralPath $HostsFile -Value "`r`n$HostsBegin`r`n$($list -join "`r`n")`r`n$HostsEnd" -Encoding Default
                ipconfig.exe /flushdns
            }
        }
        $json = Get-Content -LiteralPath $Backup -Raw | ConvertFrom-Json
        $json.Aggressive = $true
        $json | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Backup -Encoding UTF8
    }

    # Temizlik en sonda (en uzun süren adımlar)
    foreach ($t in $UserTemp, (Join-Path $env:WINDIR 'Temp')) { Get-ChildItem -LiteralPath $t -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
    Log '✓ Geçici dosyalar temizlendi'
    Step 'Disk Temizleme arka planda başlatıldı' { Start-Process cleanmgr.exe '/d C: /VERYLOWDISK' -WindowStyle Hidden }
    Log 'DISM bileşen temizliği çalışıyor (birkaç dakika sürebilir)…'
    Step 'DISM bileşen temizliği' { Dism.exe /Online /Cleanup-Image /StartComponentCleanup /Quiet }
    Log "Profil tamamlandı. Yedek: $Backup"
}

# ============================== GİRİŞ NOKTASI ==============================
# Testler betiği nokta ile yükler (. .\UMToptimizer-Windows.ps1): yalnızca fonksiyonlar tanımlanır, hiçbir şey çalışmaz.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { Write-Host 'Yönetici yetkisi gerekli. Hiçbir değişiklik yapılmadı.'; exit 1 }
    Protect-DataDir

    if (-not $Mode) {
        $Mode = switch (Read-Host "1) Standart  2) Agresif  3) Geri yükle`nSeçim") { '1' { 'Standard' } '2' { 'Aggressive' } '3' { 'Restore' } default { exit 2 } }
    }
    if (-not $Yes -and $Mode -ne 'Restore') {
        if ($Mode -eq 'Aggressive' -and (Read-Host 'Edge/OneDrive/uygulamalar kaldırılacak. Onay için AGRESIF yaz') -cne 'AGRESIF') { exit 2 }
        $RestorePoint = (Read-Host 'Geri yükleme noktası oluşturulsun mu? (E/H)') -match '^[EeYy]'
        if (Test-BitLockerOn) { $AllowBitLocker = (Read-Host 'BitLocker kapatılsın mı? Kurtarma anahtarını yedeklediysen ANAHTAR yaz') -ceq 'ANAHTAR' }
    }

    Log "=== UMToptimizer · $Mode ==="
    if ($Mode -eq 'Restore') { Restore-All; exit 0 }

    Save-Backup
    if ($RestorePoint) {
        Set-Reg 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' 'SystemRestorePointCreationFrequency' 0
        Log 'Sistem geri yükleme noktası oluşturuluyor…'
        Step 'Geri yükleme noktası oluşturuldu' {
            Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
            Checkpoint-Computer -Description 'UMToptimizer oncesi' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
        }
    }
    Apply-Profile ($Mode -eq 'Aggressive')
    exit 0
} catch {
    Log "HATA: $($_.Exception.Message)"
    exit 1
}
