# AQW Translation Mod

Tradução para português (pt-BR) do **AdventureQuest Worlds Infinity**, feita como
plugin [BepInEx](https://github.com/BepInEx/BepInEx). O mod troca os textos do
jogo em tempo real e se atualiza sozinho a partir deste repositório.

- Traduz a interface, diálogos, habilidades, itens e mapas conforme os arquivos
  de `translations/`.
- Atualiza as traduções automaticamente, **sem apagar** o que você mesmo tiver
  alterado.
- Duas edições do mesmo mod: **Player** (só traduz e atualiza) e **Translator**
  (editor dentro do jogo e envio de Pull Request).

## Instalação

Requisitos: o jogo instalado com **BepInEx 5.4.x** (Mono, 64 bits). A pasta do
jogo precisa ter `BepInEx/` e `winhttp.dll`.

1. Escolha a edição:
   - **Player** — para quem só quer jogar traduzido.
   - **Translator** — para quem vai traduzir e contribuir.
2. Copie `AQWTranslation.dll` para `BepInEx/plugins/`.
3. Abra o jogo. Na primeira vez o mod baixa as traduções sozinho (precisa de
   internet) e cria `BepInEx/plugins/translations/`.

Só uma edição deve estar instalada por vez: trocar de Player para Translator é
só substituir o `AQWTranslation.dll`. Configurações e traduções são mantidas.

## Atualizações

Uma vez por sessão, em segundo plano e sem atrasar a abertura do jogo, o mod
procura um novo release deste repositório e aplica as diferenças.

- Funciona **offline**: sem internet ou com o GitHub fora do ar, o jogo abre
  normalmente com o que já existe.
- As suas edições **não são perdidas**. O mod compara, por entrada, o que veio
  do GitHub antes, o que está no seu disco e o que chegou agora:
  - você não mexeu → entra a versão nova;
  - só você mexeu → a sua versão fica;
  - os dois mexeram → vale a do GitHub no arquivo, e a **sua versão é guardada
    em `overrides/sync_conflicts.json`** (a pasta `overrides/` tem prioridade,
    então o jogo continua mostrando a sua).
- Antes de alterar qualquer arquivo, o mod copia a versão anterior para
  `translations/_sync/backup/` (as 3 últimas atualizações ficam guardadas).
- Só chegam arquivos de dados (`.json`). Nunca código. O pacote é conferido por
  tamanho e SHA-256, e qualquer divergência cancela a atualização inteira.

Para desligar, em `BepInEx/config/com.aqwmod.localization.cfg`:

```ini
[Updates]
Enabled = false
```

## Editor e envio de traduções (edição Translator)

Aperte **F10** no jogo para abrir o editor: navegue pelos arquivos, edite, veja
os textos detectados na tela e salve. As alterações valem na hora (hot reload).

Para contribuir com o repositório:

1. Coloque as suas traduções em um arquivo de categoria (`ui/`, `maps/`,
   `skills/`...). O editor tem **Mover/Copiar** para tirar entradas do
   `uncategorized.json`.
2. No editor, abra a aba **GitHub** e clique em **Entrar com GitHub**. Anote o
   código mostrado, aprove no navegador e volte ao jogo.
3. Clique em **Revisar alterações**. O mod mostra exatamente o que será
   publicado: título, resumo por categoria, pontos de atenção e a lista das
   entradas.
4. Se estiver tudo certo, **Enviar PR**. O mod cria o fork (se necessário), a
   branch e abre a Pull Request em seu nome.

Detalhes que valem saber:

- A PR contém só o que **você** criou ou alterou em relação ao último pacote
  recebido, aplicado por cima da versão atual do repositório. O trabalho de
  outras pessoas não é desfeito.
- Os pontos de atenção apontam traduções vazias, placeholders (`{player}`,
  `{n1}`, `{map}`...) diferentes do original, tags de formatação trocadas e
  traduções automáticas ainda não revisadas.
- O login pede a permissão `public_repo` do GitHub, a menor que ele oferece para
  abrir PR. O token fica cifrado (DPAPI do Windows) em `BepInEx/config/` e
  nunca em `translations/`. Para revogar:
  [github.com/settings/applications](https://github.com/settings/applications).
- Textos em `uncategorized.json` e em `overrides/` não entram na PR.
- É preciso ter recebido ao menos um pacote do GitHub antes (o mod usa o último
  pacote como referência para saber o que mudou).

## Estrutura das traduções

```
translations/
  ui/hud.json
  maps/pirates.json
  classes/Dragonslayer.json
  uncategorized/uncategorized.json   # ainda sem categoria
  ...
```

Cada arquivo:

```json
{
  "$meta": { "locale": "pt-BR", "category": "ui/hud" },
  "translations": {
    "Play": "Jogar",
    "Attack": { "text": "Ataque", "context": "skill" },
    "Wizard": { "text": "Wizard", "ignore": true }
  }
}
```

Uma entrada é um texto ou um objeto com `text`, `context` (para desambiguar a
mesma palavra em lugares diferentes) e `ignore` (não traduzir). Placeholders
como `{player}`, `{n1}` e `{map}` precisam aparecer na tradução do mesmo jeito
que no original. A documentação técnica completa está em [`docs/`](docs/).

## Desenvolvimento

Compilar exige as DLLs do próprio jogo (`UnityEngine`, `BepInEx`...), lidas da
pasta onde este projeto fica, dentro do diretório do jogo.

```bash
# edição Translator, copiada para BepInEx/plugins automaticamente
dotnet build translation_mod.csproj

# testes (a partir da pasta Tests/)
dotnet test translation_mod.Tests.csproj

# as duas edições em dist/Player e dist/Translator, com verificação
powershell -ExecutionPolicy Bypass -File tools/build-editions.ps1
```

Mudanças em código C# só valem depois de compilar e **reiniciar o jogo**; os
arquivos de tradução recarregam sozinhos.

Ferramentas em `tools/`:

| Script | Para quê |
|---|---|
| `sync-translations.ps1` | copia as traduções da pasta do jogo para `translations/` |
| `validate-translations.ps1` | valida JSON, nomes reservados e placeholders |
| `make-release.ps1` | gera `translations.zip` + `manifest.json` |
| `build-editions.ps1` | compila Player e Translator e confere as DLLs |

### Publicar traduções

1. `tools/sync-translations.ps1`, revise o diff e faça o commit.
2. No GitHub: **Actions → Publicar traduções → Run workflow**, informando a
   versão (por exemplo `2026.10.12`; precisa ser maior que a anterior).

O mod sempre usa o release **mais recente**
(`releases/latest/download/manifest.json`). Por isso:

- todo release publicado precisa conter `manifest.json` e `translations.zip`;
- um release só com DLLs deve ser marcado como **pré-lançamento**, para não
  ser considerado o "mais recente".

## Aviso

Projeto de fãs, sem vínculo com a Artix Entertainment. AdventureQuest Worlds e
os textos originais do jogo pertencem aos seus donos.
