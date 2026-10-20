# REMOTE_UPDATES.md — Atualização automática via GitHub

> Ainda não implementado. Este documento define a
> arquitetura para que, quando implementado, não colida com nenhuma decisão
> já tomada (identidade, schema, storage em camadas).

## 1. Fluxo

```mermaid
sequenceDiagram
    participant Mod as Mod (startup, background)
    participant Local as translations/_manifest.local.json
    participant Remote as GitHub (raw/release)
    participant Staging as pasta temporária
    participant Repo as TranslationRepository

    Mod->>Local: lê manifest local (versão atual instalada)
    Mod->>Remote: GET manifest remoto (HTTPS, timeout curto)
    alt sem internet / timeout / GitHub fora do ar
        Remote-->>Mod: falha
        Mod->>Repo: segue com conteúdo local, sem bloquear o jogo
    else manifest obtido
        Mod->>Mod: compara schemaVersion + packageVersion
        alt nada novo
            Mod->>Repo: segue com conteúdo local
        else há atualização compatível
            Mod->>Remote: baixa só os arquivos com hash diferente
            Remote-->>Staging: arquivos .json em pasta temporária
            Mod->>Staging: valida JSON, tamanho, hash sha256, caminho dentro de translations/
            alt validação falha
                Mod->>Mod: descarta staging, mantém local, loga erro
            else validação ok
                Mod->>Local: move staging → translations/ (atômico, .bak da versão anterior)
                Mod->>Repo: TranslationManager recarrega (mesmo caminho do hot reload já existente)
            end
        end
    end
```

## 2. Regras inegociáveis

1. **Código só com assinatura.** O pacote de traduções traz só dados
   (`*.json` dentro de `translations/`). A única exceção é a DLL do próprio
   mod (seção `"mod"` do manifesto, `Sync/ModUpdater.cs`): ela só é baixada se
   a assinatura RSA da seção conferir com a chave pública embutida no mod
   (`ModSignature.PublicKeyXml`), e só é instalada se o arquivo bater em
   tamanho e SHA-256 com o que foi assinado. A chave privada fica no PC de
   quem publica (`tools/new-signing-key.ps1`), nunca no GitHub — quem só
   controla o repositório ou o release não consegue publicar código. Nenhum
   `.exe` ou script é baixado ou executado.
2. **Nunca bloqueia o início do jogo.** Toda a checagem/download roda em
   background (mesma disciplina de thread do resto do mod: trabalho de
   rede fora da main thread, resultado aplicado via
   `UnityMainThreadDispatcher`, reaproveitando o componente já existente).
   Se falhar por qualquer motivo, o mod segue com o conteúdo local — offline-
   first é o comportamento padrão, não um fallback de exceção.
3. **HTTPS obrigatório.** Sem exceção.
4. **Nunca sobrescreve edição local não sincronizada.** Ver §5.
5. **Uma checagem por sessão** (ou intervalo configurável, nunca a cada
   troca de cena/mapa) — resolve o "rate limit do GitHub" pela raiz, não com retry agressivo.

## 3. `_manifest.json` (proposta de formato)

```json
{
  "schemaVersion": "2.0",
  "packageVersion": "2026.09.20",
  "minModVersion": "2.2.0",
  "files": [
    {
      "path": "classes/Warrior.json",
      "sha256": "…",
      "sizeBytes": 4213,
      "updatedAt": "2026-09-18"
    },
    {
      "path": "maps/Pirates/rhuibarb.json",
      "sha256": "…",
      "sizeBytes": 2870,
      "updatedAt": "2026-09-19"
    }
  ]
}
```

Justificativa: `packageVersion` (comparação simples, monotônica — não
precisa ser semver completo), `minModVersion` (protege contra o manifesto
remoto exigir um recurso de schema que a DLL instalada ainda não entende —
nesse caso, o mod ignora o manifesto e segue local, nunca tenta "adivinhar"),
`sha256`+`sizeBytes` por arquivo (integridade e um limite de tamanho barato
de checar antes mesmo de calcular hash).

### Seção `"mod"` (atualização da DLL)

```json
"mod": {
  "version": "0.2.0",
  "editions": {
    "Player":     { "name": "AQWTranslation-Player.dll",     "sha256": "…", "sizeBytes": 99328 },
    "Translator": { "name": "AQWTranslation-Translator.dll", "sha256": "…", "sizeBytes": 302080 }
  },
  "signature": "<base64>"
}
```

A assinatura (RSA PKCS#1 v1.5 + SHA-256) cobre o texto
`ModRelease.SignedPayload`: versão e nome/sha256/tamanho de cada edição. Ela é
gerada por `tools/sign-mod-release.ps1` no `build-release.ps1`; o workflow
automático de traduções repassa a seção sem mudança, junto com as mesmas DLLs.
Seção ausente ou inválida não afeta as traduções — só não atualiza o mod.

A DLL carregada não fica travada (o Mono lê o arquivo inteiro pra memória),
então a troca acontece na hora: atual → `.old`, nova no lugar. A versão nova
roda na próxima abertura do jogo, que apaga o `.old` e mostra o aviso "o mod
foi atualizado" (`_sync/mod_state.json` guarda a última versão que rodou).

## 4. Segurança

- **Path traversal**: todo `path` do manifesto é validado para resolver
  estritamente dentro de `translations/` (rejeita `..`, caminho absoluto,
  letra de unidade, links simbólicos no destino).
- **Tamanho**: limite por arquivo e limite total do pacote, checados antes
  de gravar em disco (o manifesto já declara `sizeBytes`, então o download
  pode ser abortado cedo se divergir do prometido).
- **JSON malformado / payload gigante**: parse com limite de profundidade/
  tamanho antes de aceitar; falha de parse de um arquivo não invalida os
  demais já baixados e validados.
- **Downgrade**: um manifesto remoto com `packageVersion` menor que o local
  é ignorado (a menos que o usuário force explicitamente uma reinstalação/
  rollback pela GUI).
- **Adulteração do próprio repositório fonte**: mesmo sendo "nosso"
  GitHub, o manifesto é tratado como entrada não confiável (defesa em
  profundidade) — validação de schema, hash e caminho independe de quem
  publicou.

## 5. Conflito com alterações locais

Se `overrides/ingame_edits.json` (ou qualquer arquivo com edições locais
mais novas que `updatedAt` do manifesto) tiver sido modificado depois da
última sincronização conhecida, a atualização remota **não sobrescreve**
esse arquivo automaticamente — ele fica de fora do conjunto de arquivos
substituídos, e a GUI mostra um aviso não bloqueante ("há uma versão mais
nova de X no repositório, mas você tem alterações locais — revise
manualmente"). Arquivos sem edição local divergente são atualizados
normalmente.

## 6. Rollback

A versão anterior de cada arquivo substituído é mantida como `<arquivo>.bak`
por um ciclo de atualização. Um comando/botão "Reverter última atualização"
restaura a partir desses backups. Backups mais antigos que um ciclo são
descartados para não acumular indefinidamente.
