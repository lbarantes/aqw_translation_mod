# Gera o pacote de release das traducoes: translations.zip + manifest.json.
# Os dois arquivos sao anexados ao Release do GitHub com ESTES nomes; o mod
# baixa de releases/latest/download/manifest.json e translations.zip.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
#   powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -Version 2026.10.12.2
#
# Aplica as mesmas regras de caminho que o mod aplica ao receber o pacote
# (nada de pasta/arquivo com "_", overrides/, uncategorized.json na raiz...),
# entao o que sair daqui nao e recusado pelo cliente.
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param(
    [string]$Source = '',
    [string]$Out = '',
    [string]$Version = '',
    [string]$MinModVersion = '0.1.0',
    # secao "mod" ja assinada (tools/sign-mod-release.ps1); sem ela o release
    # so atualiza traducoes
    [string]$ModInfoFile = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Source)  { $Source = Join-Path $root 'translations' }
if (-not $Out)     { $Out = Join-Path $root 'dist\release' }
if (-not $Version) { $Version = (Get-Date).ToString('yyyy.MM.dd') }
if ($Version -notmatch '^\d{1,9}(\.\d{1,9}){0,3}$') { throw "Versao invalida: $Version (use 2026.10.12 ou 2026.10.12.2)" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Web.Extensions
$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$json.MaxJsonLength = [int]::MaxValue

$srcFull = (Resolve-Path $Source).Path.TrimEnd('\')
New-Item -ItemType Directory -Force $Out | Out-Null
$zipPath = Join-Path $Out 'translations.zip'
$manifestPath = Join-Path $Out 'manifest.json'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$utf8 = New-Object System.Text.UTF8Encoding($false)

function Test-Allowed([string]$rel) {
    $segs = $rel.Split('/')
    if ($segs | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or $_.Contains(':') }) { return $false }
    if ($segs | Where-Object { $_.StartsWith('_') }) { return $false }
    if ($segs[0] -ieq 'overrides') { return $false }
    if ($segs.Count -eq 1 -and ($segs[0] -ieq 'uncategorized.json' -or $segs[0] -ieq 'npc_names.json')) { return $false }
    return $rel.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)
}

$files = @()
Get-ChildItem $srcFull -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($srcFull.Length + 1).Replace('\', '/')
    if (-not (Test-Allowed $rel)) { return }

    # JSON invalido nao entra: o cliente recusaria o pacote inteiro.
    # (JavaScriptSerializer aceita chaves que so diferem em maiusculas, como
    # "Leg Armor!" e "Leg armor!", ao contrario do ConvertFrom-Json.)
    try { [void]$json.DeserializeObject([IO.File]::ReadAllText($_.FullName)) }
    catch { throw "JSON invalido em ${rel}: $($_.Exception.Message)" }
    $files += [pscustomobject]@{
        path      = $rel
        full      = $_.FullName
        sha256    = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        sizeBytes = $_.Length
    }
}
if ($files.Count -eq 0) { throw "Nenhum arquivo de traducao elegivel em $srcFull" }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in $files) {
        # nome da entrada com '/', criado manualmente (ZipFile.CreateFromDirectory
        # usa '\' em algumas versoes do .NET Framework)
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.full, $f.path, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zip.Dispose() }

$zipInfo = Get-Item $zipPath
$manifest = [ordered]@{
    schemaVersion  = 1
    packageVersion = $Version
    minModVersion  = $MinModVersion
    package        = [ordered]@{
        name      = 'translations.zip'
        sha256    = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        sizeBytes = $zipInfo.Length
    }
    files          = @($files | ForEach-Object { [ordered]@{ path = $_.path; sha256 = $_.sha256; sizeBytes = $_.sizeBytes } })
}
if ($ModInfoFile) {
    $manifest['mod'] = [IO.File]::ReadAllText((Resolve-Path $ModInfoFile).Path) | ConvertFrom-Json
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $utf8)

Write-Host ("Versao {0}: {1} arquivo(s), zip de {2:N0} bytes" -f $Version, $files.Count, $zipInfo.Length)
Write-Host "  $zipPath"
Write-Host "  $manifestPath"
