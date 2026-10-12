# Valida os arquivos de traducao de translations/ (usado pelo GitHub Actions
# em cada Pull Request, e tambem local).
#
#   powershell -ExecutionPolicy Bypass -File tools\validate-translations.ps1
#
# ERRO (falha a execucao):  JSON invalido; entrada que nao e texto nem objeto;
#                           arquivo fora das regras de caminho do pacote.
# AVISO (so anota):         traducao vazia; placeholders {player}/{n1}/{map}...
#                           diferentes do original; texto igual ao original
#                           (sem "ignore"); mesma chave com valores diferentes
#                           em arquivos diferentes.
# Saida no formato de anotacoes do GitHub Actions (::error / ::warning).
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param([string]$Path = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Path) { $Path = Join-Path $root 'translations' }
if (-not (Test-Path $Path)) { Write-Host "Pasta nao encontrada: $Path"; exit 1 }

Add-Type -AssemblyName System.Web.Extensions
$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$json.MaxJsonLength = [int]::MaxValue

$base = (Resolve-Path $Path).Path.TrimEnd('\')
$errors = 0; $warnings = 0
$seen = @{}   # chave -> @{ text; file }

function Annotate([string]$level, [string]$rel, [string]$msg) {
    $short = $msg.Replace("`r", ' ').Replace("`n", ' ')
    Write-Host "::$level file=translations/$rel::$short"
}

function Tokens([string]$s, [string]$pattern) {
    @([regex]::Matches($s, $pattern) | ForEach-Object { $_.Value } | Sort-Object)
}

$files = Get-ChildItem $base -Recurse -File -Filter *.json | Sort-Object FullName
foreach ($f in $files) {
    $rel = $f.FullName.Substring($base.Length + 1).Replace('\', '/')
    $segs = $rel.Split('/')

    if ($segs | Where-Object { $_.StartsWith('_') }) { continue }
    if ($segs[0] -ieq 'overrides') { continue }

    if ($segs.Count -eq 1 -and ($segs[0] -ieq 'uncategorized.json' -or $segs[0] -ieq 'npc_names.json')) {
        Annotate 'error' $rel "Nome reservado na raiz: o pacote remoto nunca entrega este arquivo. Coloque em uma subpasta."
        $errors++; continue
    }

    try { $doc = $json.DeserializeObject([IO.File]::ReadAllText($f.FullName)) }
    catch { Annotate 'error' $rel ("JSON invalido: " + $_.Exception.Message); $errors++; continue }

    if ($doc -isnot [System.Collections.IDictionary]) { Annotate 'error' $rel 'A raiz precisa ser um objeto JSON.'; $errors++; continue }

    $node = $doc
    if ($doc.ContainsKey('translations')) { $node = $doc['translations'] }
    if ($node -isnot [System.Collections.IDictionary]) { Annotate 'error' $rel '"translations" precisa ser um objeto.'; $errors++; continue }

    foreach ($key in $node.Keys) {
        if ($key -eq '$meta' -and -not $doc.ContainsKey('translations')) { continue }
        $v = $node[$key]
        $text = $null; $ignore = $false
        if ($v -is [string]) { $text = $v }
        elseif ($v -is [System.Collections.IDictionary]) {
            $text = [string]$v['text']
            $ignore = ($v['ignore'] -eq $true)
        } else {
            Annotate 'error' $rel "Entrada '$key': valor precisa ser texto ou objeto."
            $errors++; continue
        }

        if ($ignore) { continue }

        if ([string]::IsNullOrWhiteSpace($text)) {
            Annotate 'warning' $rel "Entrada '$key': traducao vazia."; $warnings++; continue
        }
        if ($text -ceq $key) {
            Annotate 'warning' $rel "Entrada '$key': igual ao original (se for de proposito, marque ignore)."; $warnings++
        }

        $ph = '\{[A-Za-z]+\d*\}'
        $a = (Tokens $key $ph) -join ','
        $b = (Tokens $text $ph) -join ','
        if ($a -ne $b) {
            Annotate 'warning' $rel "Entrada '$key': placeholders diferentes do original (original: [$a] traducao: [$b])."; $warnings++
        }

        if ($seen.ContainsKey($key) -and $seen[$key].text -cne $text -and $seen[$key].file -ne $rel) {
            Annotate 'warning' $rel "Entrada '$key': valor diferente em $($seen[$key].file). Use 'context' para desambiguar."; $warnings++
        }
        elseif (-not $seen.ContainsKey($key)) { $seen[$key] = @{ text = $text; file = $rel } }
    }
}

Write-Host ("{0} arquivo(s) verificado(s): {1} erro(s), {2} aviso(s)." -f $files.Count, $errors, $warnings)
if ($errors -gt 0) { exit 1 }
