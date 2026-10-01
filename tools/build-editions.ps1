# Gera as duas edicoes do mod em dist/Player e dist/Translator e confere que
# a edicao Player nao carrega codigo do editor.
#
# Uso (a partir da pasta translation_mod):
#   powershell -ExecutionPolicy Bypass -File tools\build-editions.ps1
#
# Nao copia nada pra BepInEx/plugins - isso so acontece no build normal
# (dotnet build), que e a edicao Translator de desenvolvimento.
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# tipos que so existem na edicao Translator
$translatorOnly = @(
    'TranslationEditorOverlay',
    'LoadExistingCapture',        # metodo do coletor concreto (a interface IMissing... fica no nucleo)
    'CaptureRegistry',
    'InputBlockerPatch',
    'InputFieldCaretFix',
    'AutoTranslationOrchestrator',
    'MyMemoryTranslationProvider',
    'TranslationFileStore',
    'IngameEditsMigrator'
)

function Build-Edition([string]$edition) {
    # caminhos relativos com '/': caminho absoluto com espacos + barra final
    # quebra as aspas na linha de comando do MSBuild
    foreach ($d in "obj/$edition", "bin/$edition") {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    }

    Write-Host "== Compilando edicao $edition"
    dotnet build translation_mod.csproj -c Release --nologo -v q `
        "-p:Edition=$edition" "-p:SkipDeploy=true" "-p:BaseIntermediateOutputPath=obj/$edition/" "-p:OutDir=bin/$edition/" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar a edicao $edition" }

    $dest = "dist/$edition"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item "bin/$edition/AQWTranslation.dll" $dest -Force
    return (Resolve-Path "$dest/AQWTranslation.dll").Path
}

# os nomes de tipo ficam como texto UTF-8 na tabela de metadados da DLL
function Has-Text([string]$path, [string]$text) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $needle = [Text.Encoding]::UTF8.GetBytes($text)
    for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
        if ($bytes[$i] -ne $needle[0]) { continue }
        $match = $true
        for ($j = 1; $j -lt $needle.Length; $j++) {
            if ($bytes[$i + $j] -ne $needle[$j]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    return $false
}

$player     = Build-Edition 'Player'
$translator = Build-Edition 'Translator'

$failed = $false
foreach ($name in $translatorOnly) {
    if (Has-Text $player $name)            { Write-Host "ERRO: Player contem '$name'"      -ForegroundColor Red; $failed = $true }
    if (-not (Has-Text $translator $name)) { Write-Host "ERRO: Translator nao tem '$name'" -ForegroundColor Red; $failed = $true }
}
if ($failed) { throw 'Verificacao das edicoes falhou.' }

"{0,-11} {1,9:N0} bytes" -f 'Player',     (Get-Item $player).Length
"{0,-11} {1,9:N0} bytes" -f 'Translator', (Get-Item $translator).Length
Write-Host 'OK: Player sem codigo do editor; Translator completo.' -ForegroundColor Green
