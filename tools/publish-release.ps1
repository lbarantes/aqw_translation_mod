# Publica um release completo no GitHub a partir DESTE PC: o build do mod
# precisa das DLLs do jogo (pasta ..\AdventureQuest Worlds Infinity_Data e
# ..\BepInEx), que nao existem nos servidores do GitHub Actions.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1 -Version 0.1.0
#   powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1 -Version 0.1.1 -Notes "Mais traducoes de mapas"
#
# Precisa: GitHub CLI com login feito uma vez (gh auth login) e Inno Setup 6.
# O release (tag v<versao>) aponta pro commit atual, que precisa ja estar no
# GitHub. Leva instalador, os dois .zip e o pacote de traducoes.
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = '',
    [string]$Repo = 'lbarantes/aqw_translation_mod'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# gh recem-instalado pelo winget nem sempre ja esta no PATH desta sessao
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) {
    $gh = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GitHub.cli*\bin\gh.exe", "$env:ProgramFiles\GitHub CLI\gh.exe" -ErrorAction SilentlyContinue |
          Select-Object -First 1 -ExpandProperty FullName
}
if (-not $gh) { throw 'GitHub CLI nao encontrado. Instale com: winget install GitHub.cli' }

# no PowerShell 5, stderr de programa externo com ErrorActionPreference=Stop
# vira excecao; aqui so importa o codigo de saida
function Test-Native([scriptblock]$cmd) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $cmd *> $null; return ($LASTEXITCODE -eq 0) } finally { $ErrorActionPreference = $prev }
}

if (-not (Test-Native { & $gh auth status })) { throw 'GitHub CLI sem login. Rode uma vez: gh auth login' }

if (git status --porcelain) { throw 'Ha alteracoes sem commit. Faca commit (e push) antes de publicar.' }
git fetch origin --quiet
$head = (git rev-parse HEAD).Trim()
if (-not (git branch -r --contains $head)) { throw 'O commit atual ainda nao esta no GitHub. Faca push antes de publicar.' }

if (Test-Native { & $gh release view "v$Version" --repo $Repo }) { throw "O release v$Version ja existe." }

& (Join-Path $PSScriptRoot 'validate-translations.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Validacao das traducoes falhou.' }
& (Join-Path $PSScriptRoot 'build-release.ps1') -Version $Version -RequireInstaller

$modVersion = [regex]::Match((Get-Content Plugin.cs -Raw), 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $Notes) { $Notes = "Mod $modVersion com o pacote de traducoes $Version." }
$template  = Get-Content (Join-Path $PSScriptRoot 'release-notes.md') -Raw -Encoding UTF8
$notesFile = Join-Path $root 'dist\release-notes.md'
[IO.File]::WriteAllText($notesFile, $template.Replace('{{NOTES}}', $Notes), (New-Object Text.UTF8Encoding($false)))

$out = Join-Path $root 'dist\release'
& $gh release create "v$Version" --repo $Repo --target $head `
    --title "AQW Translation $Version" --notes-file $notesFile `
    (Join-Path $out 'AQWTranslation-Setup.exe') `
    (Join-Path $out 'AQWTranslation-Player.zip') `
    (Join-Path $out 'AQWTranslation-Translator.zip') `
    (Join-Path $out 'translations.zip') `
    (Join-Path $out 'manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar o release.' }

Write-Host "Publicado: https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Green
