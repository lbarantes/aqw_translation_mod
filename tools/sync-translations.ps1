# Copia as traducoes de CONTEUDO da pasta do jogo (BepInEx/plugins/translations)
# para translations/ deste repositorio. E o fluxo do mantenedor: traduz no
# jogo, roda este script, revisa o diff no git e commita.
#
#   powershell -ExecutionPolicy Bypass -File tools\sync-translations.ps1            # copia
#   powershell -ExecutionPolicy Bypass -File tools\sync-translations.ps1 -DryRun    # so lista
#
# O que NAO e copiado (nao faz parte do pacote que os jogadores recebem):
#   _capture/, _sync/, overrides/, *.bak*, arquivos que comecam com "_"
#   npc_names.json (tratado a parte pelo mod)
# uncategorized.json (camada de override do jogo) vai como
#   translations/uncategorized/uncategorized.json
# porque o nome da raiz e reservado: o pacote remoto nunca escreve na camada
# de override do jogador.
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param(
    [string]$GameTranslations = '',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $GameTranslations) {
    $GameTranslations = Join-Path (Split-Path -Parent $root) 'BepInEx\plugins\translations'
}
if (-not (Test-Path $GameTranslations)) { throw "Pasta nao encontrada: $GameTranslations" }

$dest = Join-Path $root 'translations'
$src  = (Resolve-Path $GameTranslations).Path.TrimEnd('\')

$copied = 0; $unchanged = 0
Get-ChildItem $src -Recurse -File -Filter *.json | ForEach-Object {
    $rel = $_.FullName.Substring($src.Length + 1).Replace('\', '/')
    $segs = $rel.Split('/')

    if ($segs | Where-Object { $_.StartsWith('_') }) { return }
    if ($segs[0] -ieq 'overrides') { return }
    if ($rel -ieq 'npc_names.json') { return }

    $target = $rel
    if ($rel -ieq 'uncategorized.json') { $target = 'uncategorized/uncategorized.json' }

    $to = Join-Path $dest ($target.Replace('/', '\'))
    $same = (Test-Path $to) -and ((Get-FileHash $to).Hash -eq (Get-FileHash $_.FullName).Hash)
    if ($same) { $unchanged++; return }

    Write-Host ("  {0,-45} -> translations/{1}" -f $rel, $target)
    if (-not $DryRun) {
        New-Item -ItemType Directory -Force (Split-Path -Parent $to) | Out-Null
        Copy-Item $_.FullName $to -Force
    }
    $copied++
}

$verb = if ($DryRun) { 'a copiar' } else { 'copiados' }
Write-Host "$copied arquivo(s) $verb, $unchanged ja iguais."
