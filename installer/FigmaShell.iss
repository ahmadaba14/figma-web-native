; FigmaShell installer (Inno Setup 6.1+)
; Build with: .\build-installer.ps1   (publishes the app first, then compiles this script)

#define AppName "FigmaShell"
#define AppExe "FigmaShell.exe"
#define PublishDir "..\publish\win-x64"
#define AppVersion GetVersionNumbersString(PublishDir + "\" + AppExe)

[Setup]
AppId={{6B0E8C2A-3F4D-4E1B-9A57-2C8D1F0B7E43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default (no UAC); the user can still choose "all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\FigmaShell\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Close a running FigmaShell before upgrading / uninstalling.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetUrl = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe';
  DotNetFile = 'windowsdesktop-runtime-9-win-x64.exe';
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  WebView2File = 'MicrosoftEdgeWebview2Setup.exe';
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  DownloadPage: TDownloadWizardPage;
  NeedDotNet: Boolean;
  NeedWebView2: Boolean;

{ ---------- prerequisite detection ---------- }

function IsDotNetDesktop9Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function HasWebView2Key(RootKey: Integer; SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function IsWebView2Installed: Boolean;
begin
  Result := HasWebView2Key(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2Guid) or
            HasWebView2Key(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid) or
            HasWebView2Key(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Guid);
end;

{ ---------- download missing prerequisites ---------- }

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Prereqs: String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then Result := Result + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then Result := Result + MemoTasksInfo + NewLine + NewLine;

  Prereqs := '';
  if not IsDotNetDesktop9Installed then Prereqs := Prereqs + Space + '.NET 9 Desktop Runtime (x64)' + NewLine;
  if not IsWebView2Installed then Prereqs := Prereqs + Space + 'Microsoft Edge WebView2 Runtime' + NewLine;
  if Prereqs <> '' then
    Result := Result + 'Will also download and install:' + NewLine + Prereqs;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID <> wpReady then
    Exit;

  NeedDotNet := not IsDotNetDesktop9Installed;
  NeedWebView2 := not IsWebView2Installed;
  if not (NeedDotNet or NeedWebView2) then
    Exit;

  DownloadPage.Clear;
  if NeedDotNet then DownloadPage.Add(DotNetUrl, DotNetFile, '');
  if NeedWebView2 then DownloadPage.Add(WebView2Url, WebView2File, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if DownloadPage.AbortedByUser then
        Log('Prerequisite download aborted by user.')
      else
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

{ ---------- install prerequisites before copying files ---------- }

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';

  if NeedDotNet then
  begin
    { ShellExec (not Exec) so the runtime installer can raise its own UAC prompt. }
    if not ShellExec('', ExpandConstant('{tmp}\' + DotNetFile), '/install /quiet /norestart', '',
                     SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;
    if ResultCode = 3010 then
      NeedsRestart := True;
    if not IsDotNetDesktop9Installed then
    begin
      Result := 'The .NET 9 Desktop Runtime could not be installed (code ' + IntToStr(ResultCode) + ').';
      Exit;
    end;
  end;

  if NeedWebView2 then
  begin
    ShellExec('', ExpandConstant('{tmp}\' + WebView2File), '/silent /install', '',
              SW_SHOW, ewWaitUntilTerminated, ResultCode);
    if not IsWebView2Installed then
      Result := 'The Microsoft Edge WebView2 Runtime could not be installed (code ' + IntToStr(ResultCode) + ').';
  end;
end;

{ ---------- uninstall: optionally remove sign-in / session data ---------- }

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  DataDir := ExpandConstant('{localappdata}\{#AppName}');
  if DirExists(DataDir) then
    if SuppressibleMsgBox('Also remove your FigmaShell data (Figma sign-in, open tabs, cache)?' + #13#10 + DataDir,
                          mbConfirmation, MB_YESNO, IDNO) = IDYES then
      DelTree(DataDir, True, True, True);
end;
