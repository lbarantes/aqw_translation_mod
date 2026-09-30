# TRANSLATION_FORMAT.md — Schema dos arquivos de tradução

> Evolução do formato que **já existe e já funciona**
> (`{"$meta": {...}, "translations": {...}}`, visto em
> `translations/ui/hud.json`). Nenhum campo foi adicionado sem justificativa —
> cada um resolve um problema real listado em `TECHNICAL_DEBT.md` ou um
> requisito do projeto. `TranslationRepository` já sabe ler o
> formato legado (dicionário direto) e o formato com `$meta`; a extensão
> abaixo mantém os dois lendo sem erro.

## 1. Estrutura de pastas proposta

```
translations/
  overrides/
    ingame_edits.json        ← saída da GUI; PRIORIDADE MÁXIMA na resolução
  ui/
    hud.json
    menus.json
  classes/
    Warrior.json
    Mage.json
    Rogue.json
  maps/
    Pirates/
      rhuibarb.json
      port_of_lore.json
  quests/
    pirates/
      main-story.json
  npcs/
    npc_names.json            ← formato simples já existente, preservado
  _capture/                   ← gerado automaticamente, nunca editado à mão
    ui.json
    dialogue.json
    unknown.json
  _manifest.json               ← só a partir da Fase de atualização remota (ver REMOTE_UPDATES.md)
```

**A pasta é só organização.** Mover `"Attack": "Atacar"` de
`overrides/ingame_edits.json` para `classes/Warrior.json` não muda seu
significado nem exige remapear nada em runtime — a chave (texto, ou
texto+`context`) é o que importa, o arquivo é só onde a entrada mora
fisicamente. Isso é o que torna a operação "mover tradução entre arquivos"
simples de implementar: remover do dict de origem,
inserir no dict de destino, reescrever os dois arquivos.

## 2. Bloco `$meta` (por arquivo)

```json
{
  "$meta": {
    "schemaVersion": "2.0",
    "locale": "pt-BR",
    "sourceLocale": "en-US",
    "category": "classes/warrior",
    "description": "Skills e passivas do Guerreiro",
    "updatedAt": "2026-09-20",
    "author": "waldineya",
    "lastVerifiedGameVersion": "1.4.2"
  },
  "translations": { }
}
```

Justificativa campo a campo:

| Campo | Por quê existe | Por quê NÃO existe algo mais |
|---|---|---|
| `schemaVersion` | Permite ao loader futuro detectar arquivo de schema mais novo/mais antigo que o suportado e decidir migrar ou avisar. | — |
| `locale` / `sourceLocale` | Já existiam (`locale`/`source_locale`); renomeados para camelCase consistente. Necessário no momento em que houver mais de um idioma alvo, e evita um tradutor colar conteúdo no idioma errado sem perceber. | — |
| `category` | Já existia. Rótulo humano independente do caminho de pasta — sobrevive a reorganização de arquivos. | — |
| `description` | Uma linha livre para o mantenedor explicar o escopo do arquivo na GUI (tooltip da "Biblioteca"). | Não vale a pena um campo estruturado maior — é só uma nota. |
| `updatedAt` | Já existia (`last_updated`). Usado pela GUI ("traduzido há 3 dias") e pelo updater remoto para decidir se uma versão local é mais nova que a remota. | — |
| `author` | Dá crédito e prepara o terreno para um fluxo comunitário futuro sem precisar inventar um mecanismo de atribuição depois. | Aceita string ou array; não vale a pena modelar como objeto (nome+email+link) agora — YAGNI. |
| `lastVerifiedGameVersion` | Resolve a compatibilidade com versões do jogo com custo mínimo: em vez de um `compatibleGameVersion` **por entrada** (caro, e a maioria das entradas não quebra quando o jogo atualiza — a identidade por texto já falha aberto para o inglês quando o texto muda), guarda **por arquivo** a última versão do jogo em que alguém confirmou visualmente que as traduções deste arquivo ainda batem. Serve para priorizar qual arquivo revisar depois de um patch do jogo. | Um campo por entrada foi **rejeitado deliberadamente** — ver `ADR` de identidade: quando o texto original muda, a chave simplesmente deixa de casar e o jogo mostra o inglês (falha seguro), não é preciso um campo extra para isso. |

Campos deliberadamente **não incluídos** (e por quê):
- `hash`/`checksum` por arquivo — útil só quando existir distribuição remota;
  fica em `_manifest.json` (nível de pacote), não duplicado em cada arquivo.
- `priority` por arquivo — a prioridade é definida pela **camada** (pasta
  `overrides/` vs. resto), não por um número livre por arquivo, que seria
  fácil de configurar errado e difícil de auditar.

## 3. Entradas — forma simples (padrão, sem metadados)

A esmagadora maioria das entradas continua exatamente como hoje:

```json
"translations": {
  "Attack": "Atacar",
  "Defend": "Defender",
  "Quest Complete!": "Missão Completa!"
}
```

Zero mudança de comportamento e zero custo de migração para o corpus
existente.

## 4. Entradas — forma estendida (só quando precisa de metadado)

```json
"translations": {
  "Attack": {
    "text": "Ataque",
    "context": "skill",
    "status": "reviewed",
    "source": "manual",
    "ignore": false,
    "tags": ["combat", "warrior"]
  }
}
```

Justificativa campo a campo:

| Campo | Por quê existe |
|---|---|
| `text` | A tradução em si. Obrigatório na forma estendida. |
| `context` | Desambigua quando o mesmo texto-fonte precisa de traduções diferentes em lugares diferentes. **Opcional** — ausente = aplica-se ao texto em qualquer lugar (comportamento de hoje). Ver `ADR-0001`. |
| `status` | Um de `draft \| translated \| reviewed \| outdated`. Alimenta a regra de nunca sobrescrever tradução manual silenciosamente (o auto-translate confere `status`/`source` antes de tocar numa entrada) e é mostrado na Biblioteca. |
| `source` | Um de `manual \| auto \| imported`. Existe **só** para a regra de não-sobrescrita funcionar de verdade: sem saber a origem, não dá para diferenciar "peça a mão feita com cuidado" de "rascunho de API que ninguém revisou ainda". |
| `ignore` | Substitui o hack de gravar `"X":"X"` (`TECHNICAL_DEBT.md` #10). Boolean explícito, sem ambiguidade. |
| `tags` | Array livre e opcional para filtros de busca na GUI e para marcar casos especiais (ex.: `"do-not-auto-translate"`). Não obrigatório, não estruturado — é só uma etiqueta. |

Campos deliberadamente **não incluídos** por entrada (e por quê):
- **Localização do GameObject/hierarquia** (caminho, componente, cena) —
  **não é persistido no schema versionado**. É informação de captura,
  inerentemente instável entre atualizações do jogo; gravá-la em um arquivo
  que vai para controle de versão/distribuição comunitária a tornaria
  permanentemente desatualizada. Ela continua existindo — só que como dado
  **transiente**, vivo apenas em `CaptureRegistry` em memória durante a
  sessão de jogo, exibida no Inspector/GUI, nunca commitada. Isso é
  uma decisão consciente sobre como localizar novamente o elemento Unity: essa informação
  serve para **encontrar o texto na tela agora**, não para identificar a
  tradução para sempre.
- `priority` por entrada — já coberto pela camada do arquivo (§3 de
  `ARCHITECTURE.md`); um segundo nível de prioridade por entrada seria
  complexidade sem caso de uso concreto ainda.
- `hash`/`checksum` por entrada — redundante: a própria chave já é derivada
  do texto; um hash do texto não adiciona garantia nenhuma que a chave já
  não dê.
- `compatibleGameVersion` por entrada — rejeitado, ver `$meta.lastVerifiedGameVersion` acima.

## 5. Exemplos completos

### 5.1 `translations/maps/Pirates/rhuibarb.json`

```json
{
  "$meta": {
    "schemaVersion": "2.0",
    "locale": "pt-BR",
    "sourceLocale": "en-US",
    "category": "maps/pirates/rhuibarb",
    "description": "NPC Rhuibarb e diálogos da saga Pirates",
    "updatedAt": "2026-09-18",
    "author": "waldineya"
  },
  "translations": {
    "Ahoy, matey! Welcome to me ship!": "Ahoy, camarada! Bem-vindo ao meu navio!",
    "Talk to {npc} at intro to turn in this quest.": "Fale com {npc} no início para entregar esta missão.",
    "Goto map {map} to continue the quest \"{quest}\"": "Vá para o mapa {map} para continuar a missão \"{quest}\""
  }
}
```

### 5.2 `translations/classes/Warrior.json` (com desambiguação por contexto)

```json
{
  "$meta": {
    "schemaVersion": "2.0",
    "locale": "pt-BR",
    "sourceLocale": "en-US",
    "category": "classes/warrior",
    "description": "Skills e passivas do Guerreiro",
    "updatedAt": "2026-09-20",
    "author": "waldineya",
    "lastVerifiedGameVersion": "1.4.2"
  },
  "translations": {
    "Attack": {
      "text": "Ataque",
      "context": "skill",
      "status": "reviewed",
      "source": "manual",
      "ignore": false
    },
    "Deal {n1} damage to a single target.": "Causa {n1} de dano a um único alvo.",
    "Rage": "Fúria",
    "You have {n1} rage points remaining.": "Você tem {n1} pontos de fúria restantes."
  }
}
```

Contraste com `translations/ui/hud.json` (já existente), que mantém
`"Attack": "Atacar"` **sem contexto**, pois lá o mesmo texto se refere ao
botão genérico de "Ataque Automático" da barra de ação — e é exatamente essa
diferença de tradução (Ataque vs. Atacar) que motivaria, na prática, o uso do
campo `context` nesta entrada específica.

### 5.3 Tag Unity + placeholder juntos (rich text)

```json
"translations": {
  "<color=#FFD700>Welcome, {player}!</color>": "<color=#FFD700>Bem-vindo, {player}!</color>"
}
```

Aqui as tags (`<color=...>`) e o placeholder (`{player}`) convivem na
**mesma** chave/valor — isso já é suportado hoje por
`RichTextStage`+`TMPTextPatch` (strip antes do lookup, reinjeção depois) e
não muda com o novo schema.

### 5.4 `_capture/dialogue.json` (gerado automaticamente — referência, não editar manualmente)

```json
{
  "$meta": {
    "generated": "2026-09-20T14:32:10Z",
    "category": "_capture/dialogue",
    "total_count": 42,
    "note": "Preencha os valores em branco com a traducao PT-BR."
  },
  "translations": {
    "Ahoy, matey!": "",
    "Set sail for adventure!": ""
  }
}
```

Este formato **não muda** — é gerado pelo `MissingTranslationCollector`
hoje e continua no formato simples (string→string), já que seu único
consumidor é um humano decidindo o que promover para um arquivo definitivo.

## 6. `npc_names.json` (inalterado)

```json
{
  "Booker": "Bibliotecário",
  "Maya": "Maya"
}
```

Formato simples, propositalmente **fora** do padrão `$meta`+`translations`
— é uma tabela de nomes própria (chave = nome do NPC no jogo, valor = nome
traduzido ou igual para manter). Continua sem hot reload por enquanto
(`TECHNICAL_DEBT.md` #14 sugere adicionar, é uma mudança pequena e
independente).

## 7. Compatibilidade e migração

`TranslationRepository`/`CollectionLoader` precisa continuar aceitando,
nesta ordem de tentativa por arquivo:

1. Dicionário direto (`{"original":"traduzido"}`) — formato legado mais
   antigo, ainda suportado hoje.
2. `{"$meta":{...},"translations":{...}}` com valores **string** (formato
   atual de `ui/hud.json`).
3. `{"$meta":{...},"translations":{...}}` com valores **string OU objeto**
   (formato novo desta proposta) — objeto = forma estendida da §4; string =
   forma simples da §3, ambas dentro do mesmo arquivo sem problema.

Nenhum arquivo existente precisa ser reescrito para continuar funcionando.
Ver `MIGRATION.md` para o plano concreto de `ingame_edits.json` → nova
estrutura.
