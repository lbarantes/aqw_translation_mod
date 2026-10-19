# Monta o release completo em dist/release:
#   AQWTranslation-Setup.exe         instalador (escolhe Player ou Translator)
#   AQWTranslation-Player.zip        instalacao manual, so jogar
#   AQWTranslation-Translator.zip    instalacao manual, com o editor
#   translations.zip + manifest.json pacote de traducoes (auto-update do mod)
#
# Os .zip e o instalador trazem o BepInEx 5.4.23.5 (baixado do release
# oficial e conferido por SHA-256). O instalador so e gerado se o Inno
# Setup 6 estiver instalado; com -RequireInstaller a falta dele e erro (CI).
#
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 2026.10.19
#
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param(
    [string]$Version = '',
    [string]$Iscc = '',
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$BepInExVersion = '5.4.23.5'
$BepInExUrl     = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$BepInExSha256  = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'

$modVersion = [regex]::Match((Get-Content Plugin.cs -Raw), 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $modVersion) { throw 'Nao achei PluginInfo.Version em Plugin.cs' }

$out     = Join-Path $root 'dist\release'
$staging = Join-Path $root 'dist\staging'
$cache   = Join-Path $root 'dist\cache'
foreach ($d in $out, $staging) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $out, $staging, $cache | Out-Null

# 1. as duas edicoes (ja confere que a Player nao leva o editor)
& (Join-Path $PSScriptRoot 'build-editions.ps1')
foreach ($ed in 'Player', 'Translator') {
    New-Item -ItemType Directory -Force (Join-Path $staging $ed) | Out-Null
    Copy-Item "dist\$ed\AQWTranslation.dll" (Join-Path $staging $ed) -Force
}

# 2. pacote de traducoes
$makeArgs = @{ Out = $out }
if ($Version) { $makeArgs.Version = $Version }
& (Join-Path $PSScriptRoot 'make-release.ps1') @makeArgs

# 3. BepInEx oficial, conferido por hash
$bepZip = Join-Path $cache "BepInEx_win_x64_$BepInExVersion.zip"
if (-not (Test-Path $bepZip)) {
    Write-Host "== Baixando BepInEx $BepInExVersion"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $BepInExUrl -OutFile $bepZip -UseBasicParsing
}
$hash = (Get-FileHash $bepZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $BepInExSha256) {
    Remove-Item $bepZip -Force
    throw "SHA-256 do BepInEx nao confere ($hash)."
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bepDir = Join-Path $staging 'bepinex'
[IO.Compression.ZipFile]::ExtractToDirectory($bepZip, $bepDir)
Remove-Item (Join-Path $bepDir 'changelog.txt') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $bepDir 'BepInEx\plugins') | Out-Null

# 4. um .zip por edicao: extrair na pasta do jogo e pronto
foreach ($ed in 'Player', 'Translator') {
    $tree = Join-Path $staging "zip_$ed"
    Copy-Item $bepDir $tree -Recurse
    Copy-Item (Join-Path $staging "$ed\AQWTranslation.dll") (Join-Path $tree 'BepInEx\plugins') -Force
    $zip = Join-Path $out "AQWTranslation-$ed.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($tree, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "OK: $zip"
}

# 5. instalador
if (-not $Iscc) {
    foreach ($c in "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                   "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
        if (Test-Path $c) { $Iscc = $c; break }
    }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    if ($RequireInstaller) { throw 'Inno Setup 6 (ISCC.exe) nao encontrado.' }
    Write-Host 'AVISO: Inno Setup 6 nao encontrado; instalador nao gerado (os .zip foram).' -ForegroundColor Yellow
} else {
    & $Iscc /Q "/DAppVersion=$modVersion" "/DBuildDir=$staging" "/O$out" (Join-Path $PSScriptRoot 'installer\AQWTranslation.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
    Write-Host "OK: $(Join-Path $out 'AQWTranslation-Setup.exe')"
}

Write-Host "Mod $modVersion pronto em $out" -ForegroundColor Green
