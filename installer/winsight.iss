; Copyright 2026 Julien Bombled
; Licensed under the Apache License, Version 2.0.
#ifndef PayloadDir
  #error PayloadDir must point to the self-contained release directory
#endif
#ifndef SetupOutputDir
  #define SetupOutputDir "."
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{8EFBA872-AF72-4F33-973B-619BA501DDC6}
AppName=Winsight
AppVersion={#AppVersion}
AppPublisher=Julien Bombled
AppPublisherURL=https://github.com/VBlackJack/winsight
DefaultDirName={localappdata}\Programs\Winsight
DefaultGroupName=Winsight
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#SetupOutputDir}
OutputBaseFilename=Winsight-Setup-{#AppVersion}-win-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\winsight-mcp.exe
LicenseFile=..\LICENSE
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
english.Clients=Connect Winsight to these applications:
french.Clients=Connecter Winsight a ces applications :
english.Intro=Install Winsight, select your applications, then restart them. No terminal commands are required. Your applications must already be installed. Close them before continuing; existing settings are backed up. An existing Winsight entry pointing elsewhere must first be removed in that application.
french.Intro=Installez Winsight, choisissez vos applications, puis redemarrez-les. Aucune commande a saisir. Les applications doivent deja etre installees. Fermez-les avant de continuer ; les reglages existants seront sauvegardes. Une entree Winsight pointant ailleurs doit d'abord etre retiree dans l'application.
english.Finished=Winsight is installed. Restart the selected applications to use its tools. The applications may ask you to approve the connection. To add another application later, run this installer again.
french.Finished=Winsight est installe. Redemarrez les applications selectionnees pour utiliser ses outils. Elles peuvent demander votre accord pour la connexion. Pour ajouter une application plus tard, relancez cet installateur.
english.ConfigurationFailed=Winsight files are installed, but client configuration failed. Close the clients and check for an existing Winsight entry. See %%LOCALAPPDATA%%\Winsight\logs. Rerun setup to retry.
french.ConfigurationFailed=Les fichiers Winsight sont installes, mais la configuration des clients a echoue. Fermez les clients et verifiez les entrees Winsight existantes. Consultez %%LOCALAPPDATA%%\Winsight\logs. Relancez le setup pour reessayer.
english.UninstallFailed=Client configuration could not be updated. Close the clients and retry uninstalling. Winsight files have been preserved.
french.UninstallFailed=Les reglages des clients n'ont pas pu etre mis a jour. Fermez-les puis relancez la desinstallation. Les fichiers Winsight ont ete conserves.

[Tasks]
Name: "codex"; Description: "Codex (desktop / CLI)"; GroupDescription: "{cm:Clients}"; Flags: unchecked
Name: "claude_desktop"; Description: "Claude Desktop"; GroupDescription: "{cm:Clients}"; Flags: unchecked
Name: "claude_code"; Description: "Claude Code"; GroupDescription: "{cm:Clients}"; Flags: unchecked
Name: "gemini"; Description: "Gemini CLI"; GroupDescription: "{cm:Clients}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "client-registration.json"

[Icons]
Name: "{group}\Winsight documentation"; Filename: "{app}\docs\installation.html"
Name: "{group}\Uninstall Winsight"; Filename: "{uninstallexe}"

[UninstallDelete]
Type: files; Name: "{app}\client-registration.json"

[Code]
var
  ConfigurationFailed: Boolean;

procedure InitializeWizard;
var
  Selected, CodexHome, ClaudeHome, GeminiHome: String;
begin
  WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:Intro}');
  Selected := '';
  CodexHome := GetEnv('CODEX_HOME');
  if CodexHome = '' then CodexHome := AddBackslash(GetEnv('USERPROFILE')) + '.codex';
  if DirExists(CodexHome) then Selected := 'codex';
  if DirExists(ExpandConstant('{userappdata}\Claude')) then Selected := Selected + ',claude_desktop';
  ClaudeHome := GetEnv('CLAUDE_CONFIG_DIR');
  if ClaudeHome = '' then ClaudeHome := AddBackslash(GetEnv('USERPROFILE')) + '.claude';
  if FileExists(AddBackslash(GetEnv('USERPROFILE')) + '.claude.json') or DirExists(ClaudeHome) then Selected := Selected + ',claude_code';
  GeminiHome := GetEnv('GEMINI_CLI_HOME');
  if GeminiHome = '' then GeminiHome := GetEnv('USERPROFILE');
  if DirExists(AddBackslash(GeminiHome) + '.gemini') then Selected := Selected + ',gemini';
  if not WizardSilent then WizardSelectTasks(Selected);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Clients: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    Clients := '';
    if WizardIsTaskSelected('codex') then Clients := 'codex';
    if WizardIsTaskSelected('claude_desktop') then Clients := Clients + ',claude-desktop';
    if WizardIsTaskSelected('claude_code') then Clients := Clients + ',claude-code';
    if WizardIsTaskSelected('gemini') then Clients := Clients + ',gemini';
    ConfigurationFailed := not Exec(ExpandConstant('{app}\configure\winsight-configure.exe'),
      'install "' + ExpandConstant('{app}\winsight-mcp.exe') + '" "' + Clients + '"',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if not ConfigurationFailed then ConfigurationFailed := ResultCode <> 0;
    if ConfigurationFailed then begin
      Log('Client registration failed; see Winsight setup log.');
      if not WizardSilent then MsgBox(ExpandConstant('{cm:ConfigurationFailed}'), mbError, MB_OK);
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then begin
    if ConfigurationFailed then WizardForm.FinishedLabel.Caption := ExpandConstant('{cm:ConfigurationFailed}')
    else WizardForm.FinishedLabel.Caption := ExpandConstant('{cm:Finished}');
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if ConfigurationFailed then Result := 3 else Result := 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Succeeded: Boolean;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if FileExists(ExpandConstant('{app}\client-registration.json')) then begin
    Succeeded := Exec(ExpandConstant('{app}\configure\winsight-configure.exe'),
      'uninstall "' + ExpandConstant('{app}\winsight-mcp.exe') + '" manifest',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if Succeeded then Succeeded := ResultCode = 0;
    if not Succeeded then begin
      if not UninstallSilent then MsgBox(ExpandConstant('{cm:UninstallFailed}'), mbError, MB_OK);
      Abort;
    end;
  end;
end;
