# Gera a secao "mod" do manifest.json: nome, sha256 e tamanho da DLL de cada
# edicao, mais a assinatura RSA (PKCS#1 v1.5 + SHA-256) de tudo isso com a
# chave privada de tools/new-signing-key.ps1. O mod so instala uma DLL nova
# se a assinatura conferir com a chave publica embutida nele.
#
# O texto assinado precisa ser IDENTICO ao de ModRelease.SignedPayload
# (Sync/ModRelease.cs); ReleaseScriptContractTests confere os dois.
#
#   powershell -ExecutionPolicy Bypass -File tools\sign-mod-release.ps1 -Version 0.2.0 `
#       -PlayerDll dist\Player\AQWTranslation.dll -TranslatorDll dist\Translator\AQWTranslation.dll -Out dist\staging\mod.json
#
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PlayerDll,
    [Parameter(Mandatory = $true)][string]$TranslatorDll,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$KeyPath = (Join-Path $env:APPDATA 'AQWTranslation\release-signing-key.xml')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $KeyPath)) { throw "Chave de assinatura nao encontrada em $KeyPath (tools\new-signing-key.ps1)." }

# ordem ordinal dos nomes de edicao, igual ao C#
$editions = [ordered]@{}
foreach ($e in @(@{ Name = 'Player'; Path = $PlayerDll }, @{ Name = 'Translator'; Path = $TranslatorDll }) | Sort-Object { $_.Name } -CaseSensitive) {
    $item = Get-Item $e.Path
    $editions[$e.Name] = [ordered]@{
        name      = "AQWTranslation-$($e.Name).dll"
        sha256    = (Get-FileHash $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        sizeBytes = $item.Length
    }
}

$payload = "aqwtranslation-mod-release-v1`nversion=$Version`n"
foreach ($k in $editions.Keys) {
    $a = $editions[$k]
    $payload += "$k=$($a.name):$($a.sha256):$($a.sizeBytes)`n"
}

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $rsa.FromXmlString([IO.File]::ReadAllText($KeyPath))
    if ($rsa.PublicOnly) { throw "$KeyPath nao tem a chave privada." }
    $sig = $rsa.SignData((New-Object Text.UTF8Encoding($false)).GetBytes($payload), 'SHA256')
} finally { $rsa.Dispose() }

$mod = [ordered]@{
    version   = $Version
    editions  = $editions
    signature = [Convert]::ToBase64String($sig)
}
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
[IO.File]::WriteAllText($Out, ($mod | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
Write-Host "Mod $Version assinado: $Out"
