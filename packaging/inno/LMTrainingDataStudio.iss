; LLM Training Data Studio installer (Inno Setup 6+).
; Build the publish output first:
;   powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1
; Then compile this script with ISCC.exe (scripts/package-inno.ps1 finds it).

#define MyAppName "LLM Training Data Studio"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "LMTrainingDataStudio"
#define MyAppExeName "LMTrainingDataStudio2.exe"
#define PublishDir "..\..\artifacts\publish\win-x64"

[Setup]
AppId={{8B0E2B44-92B3-4A0E-B9E4-2B0C0F07A6D1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LLM Training Data Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\artifacts\installer
OutputBaseFilename=LMTrainingDataStudio-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "Associate .recipe files with LLM Training Data Studio"; GroupDescription: "File associations:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; .recipe files are opened by the app (command line accepts any .json/.recipe path).
Root: HKCU; Subkey: "Software\Classes\.recipe"; ValueType: string; ValueData: "LMTrainingDataStudio.Recipe"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\LMTrainingDataStudio.Recipe"; ValueType: string; ValueData: "LLM Training Data Studio Recipe"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\LMTrainingDataStudio.Recipe\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\LMTrainingDataStudio.Recipe\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
; Add an entry to the Windows "Open with" list for .json without hijacking the default handler.
Root: HKCU; Subkey: "Software\Classes\.json\OpenWithProgids"; ValueType: none; ValueName: "LMTrainingDataStudio.Recipe"; Flags: uninsdeletevalue; Tasks: associate

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
