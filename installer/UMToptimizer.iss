; UMToptimizer 4 — Inno Setup 6 kurulum betiği
; Derleme: ..\build.ps1  (önce dotnet publish, sonra ISCC çalıştırır)

#ifndef AppVersion
  #define AppVersion "4.1.0"
#endif
#define AppName "UMToptimizer"
#define AppExe  "UMToptimizer.exe"

[Setup]
AppId={{6E2B8E7A-4C1D-4F3B-9A57-0D7C2E5B9F41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=UMToptimizer contributors
AppPublisherURL=https://github.com/umutlab/umtoptimizer
AppSupportURL=https://github.com/umutlab/umtoptimizer/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Kullanıcı seçsin: yalnızca kendim (UAC yok) veya tüm kullanıcılar
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\src\UMToptimizer\Assets\UMToptimizer.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
tr.GetAdb=Android Platform Tools (ADB) resmi Google sunucusundan indirilsin (~7 MB)
en.GetAdb=Download Android Platform Tools (ADB) from Google (~7 MB)
tr.GettingAdb=ADB indiriliyor…
en.GettingAdb=Downloading ADB…

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "getadb"; Description: "{cm:GetAdb}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; ADB, kurulumu başlatan kullanıcının profiline indirilir (yönetici kurulumunda bile)
Filename: "{app}\{#AppExe}"; Parameters: "android install-adb"; StatusMsg: "{cm:GettingAdb}"; Tasks: getadb; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Not: %LOCALAPPDATA%\UMToptimizer (Android yedekleri, günlükler) ve %ProgramData%\UMToptimizer (Windows yedeği)
; kaldırmada bilerek SİLİNMEZ; program kaldırıldıktan sonra da geri yükleme yapılabilsin.
