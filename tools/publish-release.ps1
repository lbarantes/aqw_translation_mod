# Publica um release completo no GitHub a partir DESTE PC: o build do mod
# precisa das DLLs do jogo (pasta ..\AdventureQuest Worlds Infinity_Data e
# ..\BepInEx), que nao existem nos servidores do GitHub Actions.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1 -Version 0.1.0
#   powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1 -Version 0.1.1 -Notes "Mais traducoes de mapas"
#
# Precisa: GitHub CLI com login feito uma vez (gh auth login) e Inno Setup 6.
# O release (tag v<versao>) aponta pro commit atual, que precisa ja estar no
# GitHub. Leva instalador, os dois .zip, as DLLs avulsas (auto-update do
# mod, assinadas) e o pacote de traducoes.
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
# sem isto, um release sai sem o que ja foi mesclado no GitHub (ex.: PR de traducao)
if ([int](git rev-list --count HEAD..origin/main) -gt 0) { throw 'Sua main esta atras da do GitHub. Rode "git pull" antes de publicar.' }

if (Test-Native { & $gh release view "v$Version" --repo $Repo }) { throw "O release v$Version ja existe." }

# o validador so chama "exit" quando falha; sem zerar antes, sobraria o
# codigo do ultimo programa externo (o "gh release view" acima)
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot 'validate-translations.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Validacao das traducoes falhou.' }
# -RequireSignature: release de codigo sem assinatura nao atualizaria o mod dos jogadores
& (Join-Path $PSScriptRoot 'build-release.ps1') -Version $Version -RequireInstaller -RequireSignature

$modVersion = [regex]::Match((Get-Content Plugin.cs -Raw), 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $Notes) { $Notes = "Mod $modVersion com o pacote de traducoes $Version." }
$template  = Get-Content (Join-Path $PSScriptRoot 'release-notes.md') -Raw -Encoding UTF8
$notesFile = Join-Path $root 'dist\release-notes.md'
[IO.File]::WriteAllText($notesFile, $template.Replace('{{NOTES}}', $Notes), (New-Object Text.UTF8Encoding($false)))

# instalador, .zip, DLLs avulsas, translations.zip e manifest.json
$assets = Get-ChildItem (Join-Path $root 'dist\release') -File | ForEach-Object { $_.FullName }
& $gh release create "v$Version" --repo $Repo --target $head `
    --title "AQW Translation $Version" --notes-file $notesFile @assets
if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar o release.' }

Write-Host "Publicado: https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Green
