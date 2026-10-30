# UMToptimizer Windows tuning profiles (Windows 10/11, PowerShell 5.1+)
# Run from an elevated PowerShell window: .\UMToptimizer-Windows.ps1
[CmdletBinding()]
param(
    [ValidateSet('Standard','Aggressive','Restore')]
    [string]$Mode
)

$ErrorActionPreference = 'Continue'
$script:LogPath = Join-Path $env:ProgramData 'UMToptimizer\windows-tweaks.log'
$script:BackupPath = Join-Path $env:ProgramData 'UMToptimizer\backup.json'

function Write-Log([string]$Message) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    $dir = Split-Path -Parent $script:LogPath
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
}

function Set-RegValue([string]$Path,[string]$Name,[object]$Value,[Microsoft.Win32.RegistryValueKind]$Kind) {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType $Kind -Force | Out-Null
    Write-Log "Registry set: $Path\$Name = $Value"
}

function Save-Backup {
    $reg = @(
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; Name='EnableActivityFeed'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; Name='PublishUserActivities'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; Name='UploadUserActivities'},
        @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'; Name='DisableWpbtExecution'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location'; Name='Value'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}'; Name='SensorPermissionState'},
        @{Path='HKLM:\SYSTEM\Maps'; Name='AutoUpdateEnabled'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings'; Name='TaskbarEndTask'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; Name='EnableCdp'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='Start_TrackProgs'},
        @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power'; Name='HibernateEnabled'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings'; Name='ShowHibernateOption'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Device Metadata'; Name='PreventDeviceMetadataFromNetwork'},
        @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\3036241548'; Name='EnabledState'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'; Name='SystemRestorePointCreationFrequency'},
        @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\TimeZoneInformation'; Name='RealTimeIsUniversal'},
        @{Path='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'; Name='DisabledComponents'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications'; Name='GlobalUserDisabled'},
        @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'; Name='01'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications'; Name='ToastEnabled'},
        @{Path='HKCU:\Software\Policies\Microsoft\Windows\Explorer'; Name='DisableNotificationCenter'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\Client'; Name='RedirectionWarningDialogVersion'},
        @{Path='HKCU:\SOFTWARE\Microsoft\Terminal Server Client'; Name='RdpLaunchConsentAccepted'},
        @{Path='HKCU:\Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}'; Name='System.IsPinnedToNameSpaceTree'},
        @{Path='HKCU:\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}'; Name='System.IsPinnedToNameSpaceTree'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='LaunchTo'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='PersonalizationReportingEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='ShowRecommendationsEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='HideFirstRunExperience'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='UserFeedbackAllowed'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='ConfigureDoNotTrack'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='AlternateErrorPagesEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='EdgeCollectionsEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='EdgeShoppingAssistantEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='MicrosoftEdgeInsiderPromotionEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='ShowMicrosoftRewards'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='WebWidgetAllowed'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='DiagnosticData'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='EdgeAssetDeliveryServiceEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='WalletDonationEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Name='DefaultBrowserSettingsCampaignEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate'; Name='CreateDesktopShortcutDefault'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot'; Name='TurnOffWindowsCopilot'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'; Name='SettingsPageVisibility'},
        @{Path='HKLM:\SOFTWARE\Policies\WindowsNotepad'; Name='DisableAIFeatures'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection'; Name='AllowTelemetry'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization'; Name='DODownloadMode'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name='DisableNotificationCenter'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Dsh'; Name='AllowNewsAndInterests'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications'; Name='GlobalUserDisabled'},
        @{Path='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'; Name='01'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'; Name='VisualFXSetting'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='TaskbarAnimations'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='ListviewShadow'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='ListviewAlphaSelect'},
        @{Path='HKCU:\Software\Microsoft\Windows\DWM'; Name='EnableAeroPeek'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Name='TaskbarMn'},
        @{Path='HKCU:\Control Panel\Desktop'; Name='DragFullWindows'},
        @{Path='HKCU:\Control Panel\Desktop'; Name='MenuShowDelay'},
        @{Path='HKCU:\Control Panel\Desktop'; Name='UserPreferencesMask'},
        @{Path='HKCU:\Control Panel\Desktop\WindowMetrics'; Name='MinAnimate'},
        @{Path='HKCU:\Control Panel\Keyboard'; Name='KeyboardDelay'},
        @{Path='HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'; Name='(default)'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings'; Name='TaskbarEndTask'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching'; Name='SearchOrderConfig'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer'; Name='DisableCoInstallers'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Device Metadata'; Name='PreventDeviceMetadataFromNetwork'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; Name='EnableActivityFeed'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name='DisableWindowsConsumerFeatures'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='DisableWebSearch'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='AllowSearchToUseLocation'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name='DisableSearchBoxSuggestions'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\BraveSoftware\Brave'; Name='BraveRewardsDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveRewardsDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveWalletDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveVPNDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveAIChatEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveStatsPingEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveNewsDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveTalkDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='TorDisabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='BraveP3AEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='UrlKeyedAnonymizedDataCollectionEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='SafeBrowsingExtendedReportingEnabled'},
        @{Path='HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'; Name='MetricsReportingEnabled'}
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'; Name='SystemRestorePointCreationFrequency'},
        @{Path='HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell'; Name='FolderType'},
        @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection'; Name='AllowTelemetry'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'; Name='DisableWindowsConsumerFeatures'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name='DisableSearchBoxSuggestions'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='AllowSearchToUseLocation'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'; Name='DisableWebSearch'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'; Name='AllowTelemetry'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization'; Name='DODownloadMode'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'; Name='DisableNotificationCenter'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Dsh'; Name='AllowNewsAndInterests'},
        @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot'; Name='TurnOffWindowsCopilot'},
        @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications'; Name='GlobalUserDisabled'}
    ) | ForEach-Object {
        $key = Get-Item -LiteralPath $_.Path -ErrorAction SilentlyContinue
        $exists = $false; $value = $null; $kind = 'DWord'
        if ($key -and $key.GetValueNames() -contains $_.Name) {
            $exists = $true; $value = $key.GetValue($_.Name)
            try { $kind = $key.GetValueKind($_.Name).ToString() } catch { }
        }
        [pscustomobject]@{Path=$_.Path;Name=$_.Name;Exists=$exists;Value=$value;Kind=$kind}
    }
    $serviceNames = @('CscService','DiagTrack','MapsBroker','StorSvc','SharedAccess','WerSvc','lfsvc','RetailDemo','SysMain','WSearch','DoSvc','OneSyncSvc','WSAIFabricSvc')
    $services = foreach ($name in $serviceNames) {
        $svc = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
        if ($svc) { [pscustomobject]@{Name=$name;StartMode=$svc.StartMode;State=$svc.State} }
    }
    $bitLockerWasOn = $false
    try { $blState = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; $bitLockerWasOn = ($blState.ProtectionStatus -eq 'On' -or $blState.VolumeStatus -eq 'FullyEncrypted') } catch { }
    [pscustomobject]@{Created=(Get-Date).ToString('o');Registry=@($reg);Services=@($services);BitLockerWasOn=$bitLockerWasOn} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $script:BackupPath -Encoding UTF8
    try {
        $mp = Get-MpPreference -ErrorAction Stop
        $json = Get-Content -LiteralPath $script:BackupPath -Raw | ConvertFrom-Json
        $json | Add-Member -NotePropertyName DefenderSubmitSamplesConsent -NotePropertyValue $mp.SubmitSamplesConsent -Force
        $json | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $script:BackupPath -Encoding UTF8
    } catch { Write-Log 'Could not read Defender sample submission preference for backup.' }
    Write-Log "Settings backup saved to $script:BackupPath"
}

function Set-ServiceMode([string]$Name,[ValidateSet('Manual','Disabled','Automatic')][string]$Startup) {
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) { return }
    try {
        Set-Service -Name $Name -StartupType $Startup -ErrorAction Stop
        Write-Log "Service $Name startup set to $Startup"
    } catch { Write-Log "Could not change service $Name: $($_.Exception.Message)" }
}

function Restore-Settings {
    if (-not (Test-Path -LiteralPath $script:BackupPath)) { throw "Backup not found: $script:BackupPath" }
    $backup = Get-Content -LiteralPath $script:BackupPath -Raw | ConvertFrom-Json
    foreach ($item in $backup.Registry) {
        if ($item.Exists) {
            $kind = [Microsoft.Win32.RegistryValueKind]::$($item.Kind)
            $restoreValue = if ($item.Kind -eq 'Binary') { [byte[]]$item.Value } else { $item.Value }
            Set-RegValue $item.Path $item.Name $restoreValue $kind
        } elseif (Test-Path -LiteralPath $item.Path) {
            Remove-ItemProperty -LiteralPath $item.Path -Name $item.Name -ErrorAction SilentlyContinue
            Write-Log "Registry value removed (was absent before): $($item.Path)\$($item.Name)"
        }
    }
    foreach ($item in $backup.Services) {
        $startup = switch ($item.StartMode) { 'Auto' {'Automatic'} 'Manual' {'Manual'} 'Disabled' {'Disabled'} default {$null} }
        if ($startup) { Set-ServiceMode $item.Name $startup }
    }
    if ($backup.BitLockerWasOn -and (Read-Host 'Önceden açık olan BitLocker yeniden etkinleştirilsin mi? (E/H)') -match '^(E|EVET|Y)$') {
        Write-Host 'Şifrelemeyi yeniden başlatmak BitLocker kurtarma anahtarı gerektirebilir.' -ForegroundColor Yellow
        if ((Read-Host 'Kurtarma anahtarını yedeklediysen ANAHTAR yaz') -ceq 'ANAHTAR') {
            try { Enable-BitLocker -MountPoint $env:SystemDrive -ErrorAction Stop | Out-Null; Write-Log 'BitLocker re-enable requested.' }
            catch { Write-Log "BitLocker could not be re-enabled automatically: $($_.Exception.Message)" }
        }
    }
    $hiber = $backup.Registry | Where-Object { $_.Path -eq 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -and $_.Name -eq 'HibernateEnabled' } | Select-Object -First 1
    if ($hiber -and $hiber.Exists -and $hiber.Value -eq 1) { powercfg.exe /hibernate on | Out-Null } else { powercfg.exe /hibernate off | Out-Null }
    try { Get-NetAdapter -ErrorAction SilentlyContinue | Enable-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue } catch { }
    try { netsh.exe interface teredo set state default | Out-Null } catch { }
    try { DISM.exe /Online /Set-ReservedStorageState /State:Enabled | Out-Null } catch { }
    if ($null -ne $backup.DefenderSubmitSamplesConsent) {
        try { Set-MpPreference -SubmitSamplesConsent $backup.DefenderSubmitSamplesConsent -ErrorAction Stop; Write-Log 'Defender sample submission preference restored.' }
        catch { Write-Log 'Could not restore Defender sample submission preference.' }
    }
    Write-Log 'Restore completed. Restart Windows if requested by a setting.'
}

function Apply-Standard {
    # Enables every Essential Tweaks item shown in the user's second screenshot.
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'EnableActivityFeed' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'UploadUserActivities' 0 DWord
    Set-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' 'DisableWpbtExecution' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1 DWord
    $allFolders = 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell'
    Remove-Item 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU' -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $allFolders)) { New-Item -Path $allFolders -Force | Out-Null }
    Set-RegValue $allFolders 'FolderType' 'NotSpecified' String
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' 'Value' 'Deny' String
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}' 'SensorPermissionState' 0 DWord
    Set-RegValue 'HKLM:\SYSTEM\Maps' 'AutoUpdateEnabled' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings' 'TaskbarEndTask' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection' 'AllowTelemetry' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Start_TrackProgs' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Device Metadata' 'PreventDeviceMetadataFromNetwork' 1 DWord
    Set-RegValue 'HKLM:\SYSTEM\ControlSet001\Control\FeatureManagement\Overrides\8\3036241548' 'EnabledState' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings' 'ShowHibernateOption' 0 DWord
    Set-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' 'HibernateEnabled' 0 DWord
    try { powercfg.exe /hibernate off; Write-Log 'Hibernation disabled (hiberfil.sys removed).' } catch { Write-Log 'Could not disable hibernation.' }
    try {
        $bl = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
        if ($bl.ProtectionStatus -eq 'On' -or $bl.VolumeStatus -eq 'FullyEncrypted') {
            if ($script:AllowBitLockerDisable) {
                Disable-BitLocker -MountPoint $env:SystemDrive -ErrorAction Stop | Out-Null
                Write-Log 'BitLocker disable/decryption started.'
            } else { Write-Log 'BitLocker unchanged: recovery key confirmation not provided.' }
        }
    } catch { Write-Log "BitLocker status/action unavailable: $($_.Exception.Message)" }
    $storeDb = Join-Path $env:LocalAppData 'Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db'
    if (Test-Path $storeDb) { try { & icacls.exe $storeDb /deny '*S-1-1-0:F' | Out-Null; Write-Log 'Store recommended search database access denied.' } catch { } }
    foreach ($appxName in @('Microsoft.WidgetsPlatformRuntime','MicrosoftWindows.Client.WebExperience')) {
        Get-AppxPackage -AllUsers -Name $appxName -ErrorAction SilentlyContinue | ForEach-Object {
            try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop; Write-Log "Removed widget package $appxName" } catch { Write-Log "Could not remove widget package $appxName" }
        }
    }
    Set-ServiceMode 'CscService' Disabled
    Set-ServiceMode 'DiagTrack' Disabled
    Set-ServiceMode 'MapsBroker' Manual
    Set-ServiceMode 'StorSvc' Manual
    Set-ServiceMode 'SharedAccess' Disabled
    Set-ServiceMode 'WerSvc' Disabled
    Set-ServiceMode 'lfsvc' Disabled
    try { Set-MpPreference -SubmitSamplesConsent 2 -ErrorAction Stop; Write-Log 'Defender automatic sample submission disabled as in WinUtil Standard.' }
    catch { Write-Log 'Could not change Defender sample submission preference.' }
    foreach ($path in @($env:TEMP, (Join-Path $env:WINDIR 'Temp'))) {
        if (Test-Path -LiteralPath $path) { Get-ChildItem -LiteralPath $path -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' 'SystemRestorePointCreationFrequency' 0 DWord
    try { cleanmgr.exe /d C: /VERYLOWDISK | Out-Null } catch { }
    try { Dism.exe /online /Cleanup-Image /StartComponentCleanup /ResetBase | Out-Null } catch { Write-Log 'DISM component cleanup failed or was unavailable.' }
    Write-Log 'WinUtil Standard selected tweaks applied (Activity, Consumer Features, Explorer auto-discovery, WPBT, Location, Services, Telemetry, Delivery Optimization, Disk Cleanup, Temp files, End Task taskbar option, Restore Point).'
}

function Apply-Aggressive {
    Apply-Standard
    # Enables every Advanced Tweaks item shown in the user's first screenshot.
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search' 'AllowSearchToUseLocation' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search' 'DisableWebSearch' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'DisableNotificationCenter' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot' 1 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' 'GlobalUserDisabled' 1 DWord
    Set-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\TimeZoneInformation' 'RealTimeIsUniversal' 1 QWord
    Set-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters' 'DisabledComponents' 32 DWord
    Set-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters' 'DisabledComponents' 255 DWord
    Set-RegValue 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy' '01' 0 DWord
    Set-RegValue 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' 'DisableNotificationCenter' 1 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications' 'ToastEnabled' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\Client' 'RedirectionWarningDialogVersion' 1 DWord
    Set-RegValue 'HKCU:\SOFTWARE\Microsoft\Terminal Server Client' 'RdpLaunchConsentAccepted' 1 DWord
    Set-RegValue 'HKCU:\Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}' 'System.IsPinnedToNameSpaceTree' 0 DWord
    Set-RegValue 'HKCU:\Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}' 'System.IsPinnedToNameSpaceTree' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'LaunchTo' 1 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarAnimations' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ListviewShadow' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ListviewAlphaSelect' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\DWM' 'EnableAeroPeek' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarMn' 0 DWord
    Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting' 3 DWord
    Set-RegValue 'HKCU:\Control Panel\Desktop' 'DragFullWindows' '0' String
    Set-RegValue 'HKCU:\Control Panel\Desktop' 'MenuShowDelay' '200' String
    Set-RegValue 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate' '0' String
    Set-RegValue 'HKCU:\Control Panel\Keyboard' 'KeyboardDelay' '0' String
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'UserPreferencesMask' -Type Binary -Value ([byte[]](144,18,3,128,16,0,0,0))
    # Both IPv6 boxes are selected in the screenshot and write conflicting values.
    # Match WinUtil application order: IPv6 Disable runs after IPv4-preferred, so 255 wins.
    try { Get-NetAdapterBinding -Name '*' -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue | Disable-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue; Write-Log 'IPv6 adapter binding disabled.' } catch { }
    try { netsh.exe interface teredo set state disabled | Out-Null } catch { }
    New-Item -Path 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}' -Name 'InprocServer32' -Value '' -Force | Out-Null
    # Browser cleanup policies: disable promotions, collection, telemetry and bundled extras.
    $edgePolicy = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
    foreach ($name in @('PersonalizationReportingEnabled','ShowRecommendationsEnabled','UserFeedbackAllowed','AlternateErrorPagesEnabled','EdgeCollectionsEnabled','EdgeShoppingAssistantEnabled','MicrosoftEdgeInsiderPromotionEnabled','ShowMicrosoftRewards','WebWidgetAllowed','DiagnosticData','EdgeAssetDeliveryServiceEnabled','WalletDonationEnabled','DefaultBrowserSettingsCampaignEnabled')) { Set-RegValue $edgePolicy $name 0 DWord }
    Set-RegValue $edgePolicy 'HideFirstRunExperience' 1 DWord
    Set-RegValue $edgePolicy 'ConfigureDoNotTrack' 1 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate' 'CreateDesktopShortcutDefault' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallBlocklist' '1' 'ofefcgjbeghpigppfmkologfjadafddi' String
    $bravePolicy = 'HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'
    foreach ($name in @('BraveRewardsDisabled','BraveWalletDisabled','BraveVPNDisabled','BraveNewsDisabled','BraveTalkDisabled','TorDisabled')) { Set-RegValue $bravePolicy $name 1 DWord }
    foreach ($name in @('BraveAIChatEnabled','BraveStatsPingEnabled','BraveP3AEnabled','UrlKeyedAnonymizedDataCollectionEnabled','SafeBrowsingExtendedReportingEnabled','MetricsReportingEnabled')) { Set-RegValue $bravePolicy $name 0 DWord }
    # Windows AI, Reserved Storage, OneDrive, browser removals and hardware vendor
    # auto-install blockers are destructive/irreversible; ask before each operation.
    try { DISM.exe /Online /Set-ReservedStorageState /State:Disabled | Out-Null; Write-Log 'Reserved Storage disabled.' } catch { }
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'SettingsPageVisibility' 'hide:aicomponents' String
    Set-RegValue 'HKLM:\SOFTWARE\Policies\WindowsNotepad' 'DisableAIFeatures' 1 DWord
    Get-AppxPackage -AllUsers -Name '*Copilot*' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop } catch { } }
    Get-AppxPackage -AllUsers -Name 'MicrosoftWindows.Client.CoreAI' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop } catch { } }
    try { Disable-WindowsOptionalFeature -FeatureName Recall -Online -NoRestart -ErrorAction SilentlyContinue | Out-Null } catch { }
    try { Set-ServiceMode 'WSAIFabricSvc' Disabled } catch { }
    $onedriveSetup = Join-Path $env:WINDIR 'System32\OneDriveSetup.exe'
    if (Test-Path $onedriveSetup) { try { Start-Process -FilePath $onedriveSetup -ArgumentList '/uninstall' -Wait; Set-ServiceMode 'OneSyncSvc' Disabled; Write-Log 'OneDrive uninstaller completed.' } catch { Write-Log 'OneDrive removal failed.' } }
    $edgeAppDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application'
    $edgeSetup = Get-ChildItem -Path (Join-Path $edgeAppDir '*\Installer\setup.exe') -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($edgeSetup) { try { Start-Process -FilePath $edgeSetup.FullName -ArgumentList '--uninstall --system-level --force-uninstall --delete-profile' -Wait; Write-Log 'Edge uninstall invoked; browser profile may be deleted.' } catch { } }
    # Disable only automatic vendor helper installers; these can affect peripheral utilities.
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' 'SearchOrderConfig' 0 DWord
    Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer' 'DisableCoInstallers' 1 DWord
    try { Stop-Process -Name 'logi_download_assistant' -Force -ErrorAction SilentlyContinue } catch { }
    $programFiles64 = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    foreach ($blockPath in @((Join-Path $env:WINDIR 'Installer\Razer'),(Join-Path $programFiles64 'LogiDownloadAssistant'))) {
        if (-not (Test-Path $blockPath)) { New-Item -ItemType Directory -Path $blockPath -Force | Out-Null }
        else { Get-ChildItem -LiteralPath $blockPath -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
        try { & icacls.exe $blockPath /deny '*S-1-1-0:(W)' | Out-Null } catch { }
    }
    # Adobe block list is intentionally sourced from the same URL used by the WinUtil tweak.
    try {
        $hostsText = Invoke-RestMethod -Uri 'https://github.com/Ruddernation-Designs/Adobe-URL-Block-List/raw/refs/heads/master/hosts' -TimeoutSec 20
        Add-Content -LiteralPath (Join-Path $env:WINDIR 'System32\drivers\etc\hosts') -Value $hostsText -Encoding ASCII
        ipconfig.exe /flushdns | Out-Null
        Write-Log 'Adobe block list appended to hosts file.'
    } catch { Write-Log 'Could not download/apply Adobe block list.' }
    Set-ServiceMode 'RetailDemo' Disabled
    Set-ServiceMode 'SysMain' Disabled
    Set-ServiceMode 'WSearch' Disabled
    Set-ServiceMode 'DoSvc' Manual
    # Remove only optional consumer apps from installed/current user profiles; keep Store,
    # Photos, Calculator, media, security, and framework packages intact.
    $optionalApps = @('Microsoft.BingNews','Microsoft.BingWeather','Microsoft.GetHelp','Microsoft.Getstarted','Microsoft.MicrosoftOfficeHub','Microsoft.MicrosoftSolitaireCollection','Microsoft.People','Microsoft.Todos','Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Microsoft.YourPhone','Microsoft.ZuneMusic','Microsoft.ZuneVideo','MicrosoftCorporationII.QuickAssist','Microsoft.Windows.DevHome')
    foreach ($app in $optionalApps) {
        Get-AppxPackage -AllUsers -Name $app -ErrorAction SilentlyContinue | ForEach-Object {
            try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop; Write-Log "Removed optional app $($_.Name)" }
            catch { Write-Log "Could not remove $($_.Name): $($_.Exception.Message)" }
        }
        Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object DisplayName -EQ $app | ForEach-Object {
            try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null; Write-Log "Removed provisioned app $app" }
            catch { Write-Log "Could not remove provisioned $app: $($_.Exception.Message)" }
        }
    }
    Write-Log 'Aggressive profile applied. Review log and restart Windows.'
}

if (-not $Mode) {
    Write-Host 'UMToptimizer Windows profili'
    Write-Host '1) Normal (ikinci görseldeki Essential Tweaks)'
    Write-Host '2) Agresif (ilk görseldeki Essential + Advanced Tweaks)'
    Write-Host '3) Yedeklenen kayıt defteri/hizmet ayarlarını geri yükle'
    $choice = Read-Host 'Seçim (1-3)'
    $Mode = switch ($choice) { '1' {'Standard'} '2' {'Aggressive'} '3' {'Restore'} default { throw 'Geçersiz seçim.' } }
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'PowerShell’i Yönetici olarak açıp scripti yeniden çalıştırın. Hiçbir değişiklik yapılmadı.'
}
if ($Mode -eq 'Restore') { Restore-Settings; exit 0 }

$isAggressive = $Mode -eq 'Aggressive'
Write-Host "`nSeçilen profil: $Mode"
if ($isAggressive) {
    Write-Host 'Kaldırılacak isteğe bağlı uygulamalar: Haberler, Hava Durumu, Yardım, Solitaire, Kişiler, To Do, Geri Bildirim, Haritalar, Telefon Bağlantısı, Dev Home vb.'
    Write-Host 'İlk görseldeki tüm Advanced Tweaks uygulanır. IPv6 kapatılır, RDP uyarıları kaldırılır, Windows AI/Edge/OneDrive ve ayrılmış depolama ayarları değişir.' -ForegroundColor Yellow
    Write-Host 'Edge kaldırma profili silebilir. Disk temizliği ve uygulama kaldırmaları otomatik geri alınamaz.' -ForegroundColor Red
    if ((Read-Host 'Agresif işlemlere onay için AGGRESSIVE yaz') -cne 'AGGRESSIVE') { Write-Host 'İptal edildi.'; exit 2 }
}
Write-Host 'Normal profil de BitLocker şifresini çözebilir ve Hazırda Bekletmeyi kapatır; dizüstünde Hazırda Bekletme kullanılamaz.' -ForegroundColor Yellow
if ((Read-Host 'Önce geri yükleme noktası ve ayar yedeği oluşturup devam edilsin mi? (E/H)') -notmatch '^(E|EVET|Y)$') { Write-Host 'İptal edildi.'; exit 2 }
$script:AllowBitLockerDisable = $false
try {
    $blCheck = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
    if (($blCheck.ProtectionStatus -eq 'On' -or $blCheck.VolumeStatus -eq 'FullyEncrypted') -and
        (Read-Host 'BitLocker şifresi çözülebilir. Kurtarma anahtarını güvenli yerde sakladıysan ANAHTAR yaz') -ceq 'ANAHTAR') {
        $script:AllowBitLockerDisable = $true
    }
} catch { }
Save-Backup
Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' 'SystemRestorePointCreationFrequency' 0 DWord
try {
    if (-not (Get-ComputerRestorePoint -ErrorAction SilentlyContinue)) { Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction Stop }
    Checkpoint-Computer -Description 'UMToptimizer before tuning' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
    Write-Log 'Restore point created before applying changes.'
} catch { Write-Log 'Restore point unavailable (System Protection may be off). JSON backup remains available.' }
if ($isAggressive) { Apply-Aggressive } else { Apply-Standard }
Write-Host "`nTamamlandı. Ayar yedeği: $script:BackupPath"
Write-Host 'Geri almak için: .\UMToptimizer-Windows.ps1 -Mode Restore'
Write-Host "İşlem günlüğü: $script:LogPath"
