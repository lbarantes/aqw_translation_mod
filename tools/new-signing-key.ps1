# Cria o par de chaves que assina as atualizacoes do mod (DLL).
#
#   powershell -ExecutionPolicy Bypass -File tools\new-signing-key.ps1
#
# A chave PRIVADA fica so neste PC, fora do repositorio:
#   %APPDATA%\AQWTranslation\release-signing-key.xml
# Faca backup dela (pendrive, gerenciador de senhas). Sem ela nao da pra
# publicar versao nova do mod que as instalacoes existentes aceitem; quem a
# tiver consegue publicar uma DLL que todos os jogadores instalam sozinhos.
#
# A chave PUBLICA impressa no fim vai em Sync/ModSignature.cs (PublicKeyXml).
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param([string]$Path = (Join-Path $env:APPDATA 'AQWTranslation\release-signing-key.xml'))

$ErrorActionPreference = 'Stop'
if (Test-Path $Path) { throw "Ja existe uma chave em $Path. Nao sobrescrevo: apague a mao se tiver certeza." }

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(3072)
try {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $rsa.ToXmlString($true))
    Write-Host "Chave privada: $Path" -ForegroundColor Green
    Write-Host 'Chave publica (Sync/ModSignature.cs):'
    $rsa.ToXmlString($false)
} finally { $rsa.Dispose() }
