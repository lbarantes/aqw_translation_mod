; Instalador do AQW Translation Mod (Inno Setup 6).
;
; Gerado por tools/build-release.ps1, que passa:
;   /DAppVersion=0.1.0          versão do mod (PluginInfo.Version)
;   /DBuildDir=<pasta>          com bepinex\, Player\ e Translator\ prontos
;
; O que faz:
;   - acha a pasta do jogo pela Steam (app 2094530), em qualquer biblioteca;
;   - pergunta a edição: "Só quero jogar" (Player, padrão) ou "Quero ajudar
;     a traduzir" (Translator, com o editor F10 e envio ao GitHub);
;   - instala o BepInEx 5.4.23.5 só se ele ainda não estiver na pasta;
;   - grava BepInEx\plugins\AQWTranslation.dll da edição escolhida.
; Reinstalar com a outra opção troca de edição. Desinstalar remove só a DLL
; do mod: BepInEx, configurações e traduções ficam.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\..\dist\staging"
#endif

#define GameExe "AdventureQuest Worlds Infinity.exe"
#define SteamAppId "2094530"

[Setup]
AppId={{6E0B7C1E-3F3A-4C55-9C0B-8E0A6D1F2A47}
AppName=AQW Translation Mod (PT-BR)
AppVersion={#AppVersion}
AppVerName=AQW Translation Mod {#AppVersion}
AppPublisher=lbarantes
AppPublisherURL=https://github.com/lbarantes/aqw_translation_mod
AppSupportURL=https://github.com/lbarantes/aqw_translation_mod/issues
DefaultDirName={code:GetDefaultGameDir}
AppendDefaultDirName=no
DirExistsWarning=no
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
DisableReadyMemo=no
PrivilegesRequired=admin
; /CURRENTUSER só pra testar numa pasta sem permissão de admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=AQWTranslation-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallFilesDir={app}\BepInEx\AQWTranslation-uninstall
UninstallDisplayName=AQW Translation Mod (PT-BR)
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
SelectDirLabel3=Escolha a pasta do AdventureQuest Worlds Infinity (a que tem o "{#GameExe}").
SelectDirBrowseLabel=Se a pasta abaixo não for a do jogo, clique em Procurar.

[Files]
; BepInEx: nunca sobrescreve nem desinstala (outros mods podem depender dele)
Source: "{#BuildDir}\bepinex\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall
Source: "{#BuildDir}\Player\AQWTranslation.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion; Check: IsPlayerEdition
Source: "{#BuildDir}\Translator\AQWTranslation.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion; Check: IsTranslatorEdition

[Code]
var
  EditionPage: TInputOptionWizardPage;

function IsPlayerEdition: Boolean;
begin
  Result := EditionPage.Values[0];
end;

function IsTranslatorEdition: Boolean;
begin
  Result := EditionPage.Values[1];
end;

// valor entre aspas de uma linha VDF/ACF: "chave"   "valor"
function VdfValue(Line, Key: String): String;
var
  P: Integer;
begin
  Result := '';
  P := Pos('"' + Key + '"', Lowercase(Line));
  if P = 0 then Exit;
  Line := Copy(Line, P + Length(Key) + 2, MaxInt);
  P := Pos('"', Line);
  if P = 0 then Exit;
  Line := Copy(Line, P + 1, MaxInt);
  P := Pos('"', Line);
  if P = 0 then Exit;
  Result := Copy(Line, 1, P - 1);
  StringChangeEx(Result, '\\', '\', True);
end;

// pasta do jogo dentro de uma biblioteca da Steam, ou '' se não estiver nela
function GameDirInLibrary(Lib: String): String;
var
  Lines: TArrayOfString;
  I: Integer;
  InstallDir: String;
begin
  Result := '';
  StringChangeEx(Lib, '/', '\', True);
  if not LoadStringsFromFile(AddBackslash(Lib) + 'steamapps\appmanifest_{#SteamAppId}.acf', Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    InstallDir := VdfValue(Lines[I], 'installdir');
    if InstallDir <> '' then
    begin
      Result := AddBackslash(Lib) + 'steamapps\common\' + InstallDir;
      Exit;
    end;
  end;
end;

function FindGameDir: String;
var
  Steam, Lib: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', Steam) then
    if not RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', Steam) then
      Exit;
  StringChangeEx(Steam, '/', '\', True);

  Result := GameDirInLibrary(Steam);
  if Result <> '' then Exit;

  // bibliotecas extras (outros discos) ficam listadas aqui
  if LoadStringsFromFile(AddBackslash(Steam) + 'steamapps\libraryfolders.vdf', Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      Lib := VdfValue(Lines[I], 'path');
      if Lib <> '' then
      begin
        Result := GameDirInLibrary(Lib);
        if Result <> '' then Exit;
      end;
    end;
end;

function GetDefaultGameDir(Param: String): String;
begin
  Result := FindGameDir;
  Log('Pasta do jogo detectada: ' + Result);
  if Result = '' then
    Result := ExpandConstant('{commonpf32}\Steam\steamapps\common\AdventureQuest Worlds Infinity');
end;

procedure InitializeWizard;
begin
  EditionPage := CreateInputOptionPage(wpSelectDir,
    'Como você vai usar o mod?',
    'Dá para trocar depois: é só rodar este instalador de novo.',
    'Escolha uma opção e clique em Avançar.',
    True, False);
  EditionPage.Add('Só quero jogar traduzido (recomendado)');
  EditionPage.Add('Quero ajudar a traduzir o jogo (editor F10 e envio de traduções ao GitHub)');

  // lembra a escolha da última instalação; na primeira, "só jogar"
  if GetPreviousData('Edition', 'Player') = 'Translator' then
    EditionPage.Values[1] := True
  else
    EditionPage.Values[0] := True;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if IsTranslatorEdition then
    SetPreviousData(PreviousDataKey, 'Edition', 'Translator')
  else
    SetPreviousData(PreviousDataKey, 'Edition', 'Player');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and
     not FileExists(AddBackslash(WizardDirValue) + '{#GameExe}') then
    Result := MsgBox('Não encontrei "{#GameExe}" nesta pasta.' + #13#10 + #13#10 +
                     'Instalar mesmo assim?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Edition: String;
begin
  if IsTranslatorEdition then
    Edition := 'Tradutor (com o editor F10)'
  else
    Edition := 'Jogador (só tradução)';
  Result := MemoDirInfo + NewLine + NewLine +
            'Edição:' + NewLine + Space + Edition + NewLine + NewLine +
            'BepInEx:' + NewLine + Space;
  if FileExists(AddBackslash(WizardDirValue) + 'BepInEx\core\BepInEx.dll') then
    Result := Result + 'já instalado, não será alterado'
  else
    Result := Result + 'será instalado (5.4.23.5)';
end;
