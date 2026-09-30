# ROADMAP.md — Roadmap incremental

Cada fase lista: objetivo, mudanças, dependências, riscos, critério de
conclusão, e **o que não fazer ainda**. Fases são sequenciais por
dependência real de arquitetura, não por prazo.

---

## Fase 0 — Arquitetura e documentação (esta etapa)

**Objetivo**: entender o sistema real e documentar antes de mexer.

**Mudanças**: nenhuma no código. Produção de `docs/*.md` e `docs/adr/*.md`.

**Dependências**: nenhuma.

**Riscos**: nenhum (não toca código).

**Critério de conclusão**: os documentos desta pasta
existem e refletem o código real (verificado lendo `Plugin.cs`, `Core/`,
`Repository/`, `Pipeline/`, `Interceptors/`, `Capture/`, `Overlay/`,
`Utils/`, `Debug/` linha a linha — não suposto).

**Não fazer ainda**: qualquer alteração de código, mesmo pequena.

---

## Fase 1 — Fundamentos de segurança de dados — **CONCLUÍDA (2026-09-21)**

**Objetivo**: eliminar os riscos de perda de dados antes de qualquer
refactor maior — são os itens de menor esforço e maior retorno de
`TECHNICAL_DEBT.md`.

**Decisão do usuário**: `git init` foi explicitamente adiado — o
repositório GitHub só deve receber conteúdo quando o projeto estiver
"100%". Em substituição, versionamento por **backup manual timestampado**
(`_backup_YYYYMMDD_HHMMSS/`, mesmo padrão já usado pelo projeto em
`_backup_20260529_231856/`), feito antes de qualquer mudança de código.
Isso muda o critério de conclusão do item de histórico, mas não elimina a
proteção — só troca a ferramenta.

**Mudanças**:
- ~~`git init`~~ — **adiado por decisão do usuário**; backup manual criado
  em `_backup_20260921_102704/` antes das mudanças abaixo.
- Escrita atômica de `ingame_edits.json` (temp + `File.Replace` + `.bak`)
  (`TECHNICAL_DEBT.md` #8) — **feito**.
- `InputBlockerPatch.MouseOverOverlay` passa a cobrir o `_detailPanel`
  também, via lista `ExtraBlockedPanels` (`TECHNICAL_DEBT.md` #9) — **feito**.
- Gate de `_scan_debug.txt` por `VerboseLogging` (`TECHNICAL_DEBT.md` #6) — **feito**.

**Dependências**: nenhuma.

**Riscos**: baixíssimo — foram correções pontuais e isoladas. Build
verificado (`dotnet build`, 0 erros) após cada mudança.

**Critério de conclusão**: ~~repositório git existe com histórico~~ →
backup manual do estado pré-mudança existe e foi verificado (contagem de
arquivos bate); salvar uma tradução sobrevive a uma simulação de falha no
meio da escrita (arquivo `.tmp` não fica órfão nem corrompe o `.json`
final) — **atingido**; clicar sobre o painel de detalhe não afeta o jogo
por baixo — **atingido** (correção aplicada, validação em jogo real ainda
pendente, ver nota abaixo).

**Validado em jogo (2026-09-21)**: usuário reportou "aparentemente
funcionando" — sem problema identificado.

**Não fazer ainda**: qualquer mudança de schema, storage em camadas, ou GUI.

---

## Fase 2 — Interceptação de `UI.Text` — **CONCLUÍDA (2026-09-21, escopo revisado)**

**Objetivo**: fechar a lacuna de cobertura sem
depender do storage novo.

**Decisão do usuário**: manter a varredura periódica de 1Hz
(`ScanVisibleIntoRegistry`) e o toggle "⟳ Auto" — *"o polling permanente
(que é o que vai atualizando a lista de traduções) pode seguir... não está
causando lag nem nada do tipo"*. Isso reduz o escopo original desta fase
(ver `ADR-0004`, revisado): a substituição do timer por
`SceneManager.activeSceneChanged` e a remoção do "⟳ Auto" **não foram
feitas** e não estão mais planejadas, a menos que o usuário peça
novamente no futuro (ex.: se o corpus crescer muito e isso passar a pesar
de verdade).

**Mudanças**:
- Patch Harmony em `UnityEngine.UI.Text.text` (setter, Prefix),
  espelhando o padrão já usado em `TMP_Text.text`
  (`Interceptors/LegacyTextPatch.cs`, `TECHNICAL_DEBT.md` #1) — **feito**.
- ~~Substituir o timer perpétuo por evento de cena~~ — **não faremos**,
  decisão do usuário.
- ~~Remover o toggle "⟳ Auto"~~ — **não faremos**, decisão do usuário.

**Dependências**: nenhuma em relação à Fase 1.

**Riscos**: médio — mudar a cobertura de interceptação é sensível
(comportamento visível do jogo). Build verificado (`dotnet build`, 0
erros); **teste manual em sessão real ainda pendente** (ambiente não
permite rodar o jogo) — verificar especificamente popups/diálogos legados
conhecidos por usarem `UI.Text` (ex.: `"Goto map..."`).

**Critério de conclusão**: build limpo — **atingido**. Popups como
`"Goto map..."` deveriam agora traduzir imediatamente ao aparecer, sem
esperar o próximo tick de varredura — **pendente de confirmação em jogo**.

**Não fazer ainda**: consolidação de normalização duplicada
(`TECHNICAL_DEBT.md` #4) — fica para a Fase 3, junto do storage, porque as
duas mudanças tocam os mesmos arquivos (`TMPTextPatch`, `Pipeline/Stages`).

---

## Fase 3 — Identidade com contexto + storage em camadas — **CONCLUÍDA PARCIALMENTE (2026-09-21)**

**Objetivo**: implementar `ADR-0001` (identidade) e `ADR-0002` (storage),
que são o núcleo do projeto.

**Mudanças**:
- Novo schema (`TRANSLATION_FORMAT.md`): `context`, `status`, `source`,
  `ignore`, `tags`, `$meta` estendido — retrocompatível — **feito**
  (`Core/TranslationEntry.cs`, `TranslationRepository.LoadFile`).
- ~~`CollectionLoader`~~ + `TranslationIndex` (proveniência arquivo→chave,
  `ARCHITECTURE.md` §4) — **feito**, com uma simplificação: não foi criada
  uma classe `CollectionLoader` separada — a lógica de descoberta/merge de
  arquivos ficou dentro de `TranslationRepository.LoadAll`/`MergeLayer`
  (uma classe nova só para isso seria abstração sem necessidade real hoje,
  já que só há um consumidor). `TranslationIndex` existe como classe própria
  (`Core/TranslationIndex.cs`), exposta via `ITranslationRepository.Index`.
- Resolução por camada de prioridade (`overrides/` sempre vence) com log de
  conflito em vez de last-write-wins silencioso (`TECHNICAL_DEBT.md` #3) —
  **feito**.
- ~~`TranslationResolver`~~ (texto+contexto opcional → chave efetiva,
  `ARCHITECTURE.md` §2) — **feito**, também sem uma classe própria: virou
  o método `TranslationRepository.TryGet(key, context, out value)` +
  `Core/TranslationKey.cs`, propagado por `LookupStage`. Mesma razão da
  simplificação acima — poucas linhas, um consumidor, não precisa de classe
  dedicada ainda.
- ~~Consolidação da normalização de placeholders/rich text em
  `Pipeline/Stages`~~ (`TECHNICAL_DEBT.md` #4) — **deliberadamente NÃO
  feito nesta passada**. É o item de maior risco desta fase por definição
  própria (mexe no hotpath de tradução inteiro, sem forma de testar em
  jogo real neste ambiente) — decidido tratar como uma fase dedicada
  futura, com o usuário testando cada caso em jogo, em vez de empacotar
  junto com o resto de uma vez.
- Ferramenta de migração de `ingame_edits.json` (`MIGRATION.md`) — **feito**
  (`Migration/IngameEditsMigrator.cs`), mas **não invocada** — não há botão
  na GUI ainda (isso é Fase 4) e a classe não roda sozinha em lugar nenhum,
  por respeito à regra de nunca alterar dados do usuário sem pedido
  explícito.

**Achado durante a implementação (não estava no escopo original)**:
`TECHNICAL_DEBT.md` #16 — o carregador antigo **descartava** entradas
"ignoradas" (`"X":"X"`) em vez de armazená-las, fazendo com que
`MissingTranslationCollector` as recapturasse para sempre como "faltando
tradução". Corrigido junto da reescrita do parser (mesma função já estava
sendo tocada). Não muda nada visível ao jogador — só para de gerar ruído
em `_capture/*.json` para textos já marcados como ignorados.

**Dependências**: Fase 1 (backup manual — usado antes desta fase) e
Fase 2 (para não reabrir a lógica de `TMPTextPatch` duas vezes — respeitado:
`TMPTextPatch` não foi tocado nesta fase).

**Riscos**: alto (blast radius grande — toca o carregamento de TODA
tradução). Mitigação aplicada: validado por leitura cuidadosa linha a linha
contra o corpus real do usuário (`BepInEx/plugins/translations/`, 927
linhas em `ingame_edits.json` + múltiplos arquivos de conteúdo), não só
contra dados de exemplo; build limpo (`dotnet build`, 0 erros).
**Validado em jogo (2026-09-21)**: usuário reportou "não vi nenhuma
mudança visível" — resultado **esperado e correto**: esta fase mexeu só em
como as traduções são carregadas/priorizadas por baixo dos panos (schema,
identidade com contexto, camadas de prioridade), sem nenhuma intenção de
mudar o que aparece na tela do jogo.

**Critério de conclusão**: ~~todas as strings da checklist de regressão
traduzem exatamente como antes~~ → não se aplica (normalização não foi
tocada); build limpo — **atingido**; uma entrada com `context` resolve
corretamente quando dois textos idênticos coexistem — **atingido no
código, sem teste em jogo**; ferramenta de migração pronta e não-destrutiva
— **atingido** (implementada, não executada).

**Não fazer ainda**: reforma de GUI, auto-tradução, atualização remota,
consolidação de normalização (ver acima).

---

## Fase 4 — GUI em camadas + Biblioteca completa + Settings — **CONCLUÍDA PARCIALMENTE (2026-09-21)**

**Objetivo**: `GUI_ARCHITECTURE.md` — separar ViewModel de renderização,
completar a Biblioteca para navegar o corpus inteiro (não só
`ingame_edits.json`), adicionar aba de Configurações.

**Mudanças**:
- ~~Extração de `TranslationBrowserViewModel`/`LibraryViewModel`/
  `SettingsViewModel`~~ — **NÃO feito nesta passada**. É o refactor mais
  invasivo desta fase (reorganiza a classe inteira sem mudar
  comportamento) e o de menor retorno imediato — decidido priorizar as
  duas mudanças com valor direto para o usuário (Biblioteca completa,
  Settings) e deixar a separação de camadas para quando houver um segundo
  consumidor real da lógica (ex.: um reskin visual) que a justifique.
- Biblioteca lê `TranslationIndex` (todo o corpus, navegável por
  categoria/pasta via chips) — **feito**. `DoLibrary()` agora itera
  `Repository.Index.SourceFile` inteiro, resolve cada chave (com contexto,
  se houver) via `TranslationRepository.TryGet`, e cada linha carrega seu
  arquivo de origem real (`EntryRow.SourceFile`).
- Salvar/Ignorar/Del na Biblioteca escrevem de volta no arquivo real de
  origem (`Repository/TranslationFileStore.cs`, novo), não mais sempre em
  `ingame_edits.json` — **feito**.
- ~~Operações de mover/copiar/importar/exportar entre arquivos~~ — **NÃO
  feito nesta passada**. Ficou de fora para não inflar ainda mais o
  escopo desta fase; a infraestrutura (`TranslationFileStore.Upsert`/
  `Remove` em qualquer arquivo) já é o suficiente para implementar "mover"
  como uma combinação de `Remove` na origem + `Upsert` no destino quando
  for priorizado.
- Aba de Configurações — **feito**, com escopo restrito ao que já tem
  efeito real hoje (Enabled, CacheCapacity, CaptureEnabled/MinLength/
  FlushMins, EnforceOnScan, HotReloadEnabled/Debounce, VerboseLogging,
  LogMisses, ShowStatsOnExit) — nada de idioma alvo/provedor de auto-tradução/
  API key ainda, porque esses recursos não existem de verdade até a Fase 5;
  adicionar UI para eles agora seria construir para uma funcionalidade que
  ainda não existe.

**Achado durante a implementação (não estava no escopo original)**:
`TECHNICAL_DEBT.md` #17 — o `ReadFile`/`WriteFile` antigos do overlay
(leitor de tokens escrito à mão) corrompiam silenciosamente qualquer
entrada em forma de objeto que aparecesse em `ingame_edits.json` — um
risco real assim que a Biblioteca passou a poder gravar essa forma no
mesmo arquivo que o Scan também escreve. Descoberto e corrigido **antes**
de qualquer dado real ser afetado (identificado por raciocínio sobre o
código, não por reprodução em jogo) — ambos agora delegam para
`TranslationFileStore`.

**Dependências**: Fase 3 (a Biblioteca completa só faz sentido com
`TranslationIndex` existindo) — respeitado.

**Riscos**: médio, como previsto — mitigado por não ter feito o refactor
de ViewModel (que teria ampliado a superfície de risco sem necessidade) e
por ter caçado ativamente por bugs de corrupção de dados antes de
considerar a fase concluída (resultou no achado do item #17). Build
verificado (0 erros).

**Validado em jogo (2026-09-21)**: a Biblioteca mostrou corretamente
categorias de múltiplos arquivos — "(raiz)" (`ingame_edits.json`), "guis",
"screens", "ui" — e o filtro por chip funcionou como esperado. Achado um
problema real: abrir a Biblioteca causava um "lagzinho" perceptível por
causa do volume de conteúdo (o corpus real do usuário tem milhares de
entradas). Investigado e corrigido no mesmo dia — ver
`docs/TECHNICAL_DEBT.md` #18: a construção das linhas de UI (antes toda de
uma vez, ~19 `GameObject`s por linha) agora é espalhada ao longo de vários
frames via corrotina. Ainda não reconfirmado em jogo após a correção.

**Critério de conclusão**: ~~todas as funcionalidades da GUI atual
continuam funcionando idênticas~~ — **atingido no código** (Scan/Detalhe/
Inspetor inalterados); a Biblioteca mostra entradas de qualquer arquivo do
corpus — **atingido**; ~~mover uma entrada entre arquivos funciona de
ponta a ponta~~ — **não implementado nesta fase** (ver acima).

**Não fazer ainda**: reforma visual (cores/tipografia/tema AQW), separação
em ViewModel, mover/copiar/importar/exportar (deferidos, ver acima).

---

## Fase 5 — Tradução automática — **CONCLUÍDA (2026-09-21)**

**Objetivo**: `AUTO_TRANSLATION.md` — `ITranslationProvider`, orquestrador,
UX de preview/confirmação.

**Mudanças**:
- `Translation/ITranslationProvider.cs` — interface enxuta (sem
  `TranslateBatchAsync`/`MaxBatchSize` como a proposta original tinha —
  simplificado por não haver ainda nenhum provedor com lote de verdade
  para justificar isso na interface; ver nota no próprio arquivo).
- `Translation/ManualTranslationProvider.cs` — no-op, usado quando
  `AutoTranslateEnabled=false`.
- `Translation/MyMemoryTranslationProvider.cs` — provedor público real
  (mymemory.translated.net), sem necessidade de chave, via `HttpClient`
  assíncrono (nova referência `System.Net.Http` no `.csproj`).
- `Translation/AutoTranslationOrchestrator.cs` — rate limiting (400ms
  entre chamadas), retry (uma tentativa extra com atraso), cache de
  sessão, cancelamento (`CancellationToken`), e a validação de
  placeholders (conta `{` antes/depois — se não bater, marca como
  suspeito e a GUI desabilita o checkbox daquela linha no preview).
  Reaproveita `Utils/RichTextUtils.cs` para proteger tags Unity antes de
  enviar ao provedor, exatamente como planejado (não uma reimplementação
  paralela).
- Botão "Auto Traduzir" no rodapé da GUI + painel de preview
  (`Overlay/TranslationEditorOverlay.cs`) — opera sobre as linhas
  atualmente VISÍVEIS (respeitando os filtros de busca/categoria já
  existentes, que fazem o papel de "seleção" sem precisar de checkboxes
  extras na lista principal já cheia), até 15 por vez. Cada linha do
  preview tem checkbox próprio, desmarcado por padrão quando já existia
  tradução manual (nunca sobrescreve sem confirmação explícita).
- `ModConfig`: `AutoTranslateEnabled`/`AutoTranslateSourceLang`/
  `AutoTranslateTargetLang`, editáveis na aba Configurações (Fase 4).
- `TranslationFileStore.Upsert` ganhou um parâmetro `source`, usado para
  gravar `source:"auto"` nas entradas aplicadas pela Biblioteca via Auto
  Tradução (Scan continua sem essa marcação — mesma assimetria já
  documentada em `TECHNICAL_DEBT.md` #10).

**Bug pego DURANTE a implementação, antes de compilar/enviar** (não chegou
a existir em nenhuma versão funcional — registrado aqui só por ser uma
armadilha real que outro desenvolvedor poderia repetir): a primeira versão
mandava `EntryRow.RawOriginal` para a API. No modo Scan, `RawOriginal`
contém o valor REAL capturado (ex.: um username de verdade), não o token
`{player}` — mandar isso geraria uma tradução com um valor específico
"grudado" nela, quebrando a experiência de todo jogador cujo valor real
fosse diferente. Corrigido para sempre usar `EntryRow.Original` (a chave
já normalizada com tokens) tanto como identificador quanto como texto
enviado — com comentário no código explicando por quê, para não se repetir.

**Dependências**: Fase 4 (o botão se encaixa na barra de ações já
existente) e Fase 3 (o campo `source` do schema precisava existir) —
respeitadas.

**Riscos**: médio, como previsto. Mitigado por: nunca aplicar nada sem
confirmação explícita por linha; nunca aplicar entradas com contagem de
placeholder inconsistente; rate limiting para não estourar o limite
gratuito do provedor de uma vez.

**Validado em jogo (2026-09-21)**: usuário reportou "Auto traduzir
funcionando" — o fluxo completo (marcar linhas → preview → aplicar) foi
exercitado contra a API real do MyMemory com sucesso.

**Critério de conclusão**: selecionar (via filtro) → preview → aplicar
marcadas funciona fim a fim no código, com um provedor real configurado
por padrão (MyMemory) — **atingido no código, teste em jogo pendente**;
nenhuma tradução manual é sobrescrita sem confirmação explícita por linha
— **atingido** (checkbox desmarcado por padrão quando já existia tradução).

**Não fazer ainda**: atualização remota, instalador, comunidade.

---

## Fase 6 — Atualização remota

**Objetivo**: `REMOTE_UPDATES.md` — manifesto, download seguro, offline-first.

**Mudanças**: `_manifest.json`, cliente de atualização (HTTPS, validação,
staging, troca atômica, rollback), integração com o repositório GitHub
público do projeto (quando existir).

**Dependências**: Fase 3 (o schema precisa de `schemaVersion` estável para
o manifesto poder declarar compatibilidade) e a existência de um
repositório GitHub público real com conteúdo (fora do controle deste
roadmap técnico).

**Riscos**: alto em superfície de segurança (dados vindos da internet) —
mitigado pelas regras de segurança (path traversal, hash, tamanho,
nunca executar código).

**Critério de conclusão**: com o mod offline, o jogo inicia normalmente com
o conteúdo local; com internet e uma atualização disponível, o conteúdo é
baixado, validado e aplicado sem reiniciar o jogo (reusando o hot reload já
existente); uma atualização corrompida/adulterada é rejeitada sem afetar o
conteúdo local.

**Não fazer ainda**: instalador, bot do Discord, fluxo de PR comunitário.

---

## Fase 7 — Plataforma comunitária (não implementar agora)

**Objetivo**: só **preparar o terreno** — nenhuma implementação.

Decisões já tomadas nas fases anteriores que ajudam esta fase futura, sem
custo extra hoje:
- `author` no `$meta` (atribuição pronta para um fluxo de PR).
- Schema versionado e validável (facilita revisão automatizada de PR).
- `TranslationIndex`/camadas de prioridade (facilita decidir onde uma
  contribuição externa deveria entrar).
- Formato de arquivo por escopo pequeno (`Maps/Pirates/rhuibarb.json`) em
  vez de um arquivo monolítico — PRs menores e revisáveis por natureza.

**Não fazer ainda**: bot, site, fluxo de aprovação, merge automatizado —
tudo isso é decisão de produto/comunidade fora do escopo técnico atual.

---

## Fase 8 — Instalador (não implementar agora)

**Objetivo**: só identificar requisitos, sem construir nada.

Requisitos já visíveis a partir do código atual: detectar a pasta do jogo
(mesmo padrão de `ResolveTranslationsPath`), verificar/instalar BepInEx se
ausente, copiar `AQWTranslation.dll` + `translations/` para
`BepInEx/plugins/AQWTranslation/`, opcionalmente rodar a migração da Fase 3
numa instalação existente. Nada disso foi construído ainda.

---

## Revamp de UI (pedido em 2026-09-22) — F10 vira árvore de arquivos + janela de Scan separada

**Objetivo do usuário**: em vez do F10 abrir direto a lista de textos
escaneados, ele deve abrir uma árvore de pastas/arquivos de `translations/`
(criar pasta, criar arquivo, expandir, abrir arquivo → tabela de traduções
daquele arquivo). Um botão separado ("Textos detectados na tela") abre a
GUI de Scan de hoje numa janela própria. Do Scan, arrastar (ou de forma
igualmente prática) uma linha JÁ TRADUZIDA para um arquivo aberto.

Planejado em etapas grandes, mas verificáveis (aprovado pelo usuário
2026-09-22), cada uma testada em jogo antes da próxima:

1. **Separar o Scan da janela principal** — ✅ **feito (2026-09-22)**.
   Nova janela standalone ("Textos detectados na tela", botão na janela
   principal), com `RowListPanel` próprio (lista/filtro/seleção/buffers
   independentes — ver `Overlay/TranslationEditorOverlay.ScanWindow.cs`).
   A janela principal (F10) continua EXATAMENTE como antes por enquanto
   (ainda mostra Scan/Biblioteca/Config/Inspetor) — a nova janela é
   ADITIVA, não substitui nada ainda, para não arriscar nada do que já
   funciona. Isso significa que, temporariamente, é possível ver os
   mesmos textos capturados tanto no F10 (modo Scan) quanto na nova
   janela — redundância esperada, resolvida na Etapa 6.
   - Refactor de suporte necessário (descoberto ao implementar, não
     previsto no plano original): `BuildRow`/`PopulateRows`/
     `RefreshVisibility`/`RebuildCategoryChips`/`ToggleUntrOnly`/
     `UpdateSaveAllBtn`/`SaveAll`/`SelectAllVisible`/`ClearSelection`/
     `SetRowSelected`/`OnAutoTranslateClicked`/`MoveOrCopySelected`
     agora recebem um `RowListPanel` (em vez de ler campos fixos da
     classe) — permite duas listas de linhas simultâneas sem duplicar
     nenhuma regra de negócio. `Settings`/`Inspector`/`ToggleLibrary`
     não precisaram mudar (não usam esses métodos compartilhados).
   - Build limpo, 134 testes automatizados continuam passando (nenhum
     cobre a janela em si — ver nota abaixo), backup em
     `_backup_20260922_153242`.
2. Árvore de pastas/arquivos (só leitura) no F10, substituindo a lista de
   Scan — ✅ **feito (2026-09-22)**. Dado vem de `TranslationIndex.KeysByFile`
   via `Overlay/FileTreeBuilder.cs` (puro, testado — `Tests/FileTreeBuilderTests.cs`,
   7 testes); UI em `Overlay/TranslationEditorOverlay.Tree.cs`. Novo
   `ViewMode.Tree` virou o padrão do F10 (era `Scan`) — "↺ Refresh" e o
   botão "Bib." agora alternam entre Árvore e Biblioteca, não mais
   Scan/Biblioteca. Só navegação (expandir/colapsar pasta) — abrir arquivo
   é a Etapa 3. Build limpo, 141 testes passando, backup em
   `_backup_20260922_155113`.
   - **Débito conhecido, não resolvido de propósito**: os botões "⟳ Auto"/
     "⊕ Acum." na barra de filtro do F10 ficaram órfãos (eram específicos
     do Scan-in-window, que não é mais o padrão) — continuam clicáveis
     mas não fazem mais nada. Baixo risco (não quebram nada, só
     cosmeticamente redundantes) — limpar na Etapa 6 junto com o resto da
     UI antiga.
3. Abrir arquivo → janela com tabela de traduções daquele arquivo —
   ✅ **feito (2026-09-22)**. Botão "Abrir" em cada linha de arquivo da
   árvore abre uma janela independente (`OpenFileTable`/
   `BuildFileTableWindow`/`PopulateFileTable` em
   `Overlay/TranslationEditorOverlay.FileTable.cs`), com seu próprio
   `RowListPanel` — reaproveita 100% da máquina de linhas já generalizada
   na Etapa 1 (BuildRow/SaveEntry/IgnoreEntry/Del/SaveAll/SelectAllVisible/
   ClearSelection/OnAutoTranslateClicked/MoveOrCopySelected), nenhuma
   lógica de negócio nova. Múltiplas janelas de arquivo podem ficar
   abertas ao mesmo tempo (cascata simples de posição); abrir o mesmo
   arquivo de novo só traz a janela existente pra frente em vez de
   duplicar. Dado vem de `Overlay/FileTableEntryListBuilder.cs` (puro,
   testado — `Tests/FileTableEntryListBuilderTests.cs`, 4 testes) via
   `TranslationFileStore.Read` (lê o arquivo puro, não passa pelas
   camadas de prioridade do repositório — faz sentido aqui, o usuário
   está editando ESTE arquivo específico). Build limpo, 0 avisos, 145
   testes passando, backup em `_backup_20260922_162444`.
   - Aproveitei o build desta etapa pra limpar código que tinha ficado
     genuinamente morto na Etapa 2 (não só "cosmético"): `DoScan()` do
     modo Scan-in-window, `QuickCheckAndRescan()`, e os campos
     `_autoRefresh`/`_autoRefreshTimer`/`_accumulate`/`_imgAutoRef`/
     `_imgAccum`/`_lastSeenTexts`/`_lastRegistryCount` — confirmados sem
     nenhum call site alcançável (o compilador já sinalizava
     `_imgTodas`/`_categoryChipImgs` como não usados; ao investigar achei
     mais dois pontos que a Etapa 2 tinha esquecido de atualizar:
     `ToggleInspect()`/`ToggleSettings()` ainda voltavam pro Scan antigo
     ao sair do Inspetor/Configurações em vez da árvore — corrigido
     junto.
4. Criar pasta / criar arquivo pela árvore — ✅ **feito (2026-09-22)**.
   Botões "+P"/"+A" em cada linha de pasta da árvore (cria dentro daquela
   pasta) e "+ Pasta"/"+ Arquivo" no cabeçalho da janela principal (cria na
   raiz de `translations/`) abrem um modal compartilhado
   (`Overlay/TranslationEditorOverlay.CreateNode.cs`) que pede o nome e
   valida via `Overlay/NewNodeNameValidator.cs` (puro, testado —
   `Tests/NewNodeNameValidatorTests.cs`, 12 testes: path traversal,
   caminho absoluto, caracteres inválidos, barras normalizadas, ".json"
   automático em arquivo). Criar arquivo usa
   `TranslationFileStore.EnsureFileExists` (novo, idempotente, mesmo padrão
   de escrita atômica já existente) + `TranslationManager.ReloadFile` (pra
   aparecer na árvore imediatamente, sem reiniciar o jogo) e já abre a
   tabela do arquivo recém-criado. Criar pasta usa `Directory.CreateDirectory`
   direto — como `TranslationIndex.KeysByFile` só reflete arquivos
   carregados, uma pasta vazia não apareceria sozinha na árvore; resolvido
   com `FileTreeBuilder.BuildRoot` ganhando um parâmetro opcional
   `extraFolders` (`Tests/FileTreeBuilderTests.cs`, +4 testes) e a árvore
   lembrando localmente (`_treeCreatedFolders`, em memória, dura a sessão)
   quais pastas foram criadas sem arquivo ainda — limitação aceita: uma
   pasta criada e deixada vazia até o fim da sessão do jogo desaparece da
   árvore no próximo restart (o fluxo esperado é criar a pasta e já criar
   um arquivo dentro dela). Build limpo, 163 testes passando (+18), backup
   em `_backup_20260922_175407`.
5. Mover texto traduzido do Scan para um arquivo aberto — começar com um
   botão "Enviar para..." simples, só tentar arrastar-e-soltar de verdade
   depois de validado (maior risco técnico do revamp) — **pendente**.
6. Aposentar a Biblioteca antiga (a árvore+tabela cobre tudo que ela
   fazia) — **pendente**.

**Nota sobre testes automatizados**: a construção de UI em si (Etapa 1
incluída) não é testável automaticamente sem um Unity Editor/Player — só
a lógica que ela usa por baixo (filtro, seleção, ordenação, etc., já
cobertas por `Tests/`) é. Cada etapa continua exigindo confirmação manual
do usuário em jogo antes da próxima.

---

## Reskin visual com assets reais do AQW (pedido em 2026-09-22)

**Objetivo do usuário**: o overlay usa uGUI legado com retângulos de cor
lisa (sem sprite nenhum) e a fonte builtin do Unity — "parece um sistema
legado antigo". Pedido: revamp visual na pegada AQW, o mais rápido
possível, minimizando trabalho manual do usuário.

**Pesquisa que motivou a abordagem** (2026-09-22, antes de escrever
qualquer código): confirmado via `VersionInfo` de `UnityPlayer.dll` que o
jogo roda em **Unity 6000.3.17f1**, backend **Mono** (não IL2CPP — sem
`GameAssembly.dll`, `"ScriptingBackend":"Mono2x"` no metadata). Ambos
`UnityEngine.UI.dll` (uGUI, já usado) e `UnityEngine.UIElementsModule.dll`
(UI Toolkit) estão presentes no build — migrar de lib seria tecnicamente
viável, mas **decidido não migrar**: reescrever os ~200 call sites de
Go/HRow/Lbl/Btn/Fld do zero é caro pra um ganho que dá pra conseguir bem
mais barato dentro do uGUI atual.

Uma varredura de strings em `resources.assets` (19MB, binário do jogo,
sem precisar rodar nada) confirmou que o próprio AQW já tem um kit de UI
completo carregado no build: painéis 9-slice ("9 Slice Main Window
Background Round", "AQ2DRoundedCornerPanelBG"), botões ("AcceptButton",
"9 Sliced Red Button", "9-slice-simple-button"), ícones (`APOPIcon_*`) e
até uma fonte TMP no estilo certo ("TrajanPro3-Regular SDF Atlas"). Esses
nomes existem no build, mas só ficam disponíveis via
`Resources.FindObjectsOfTypeAll<Sprite>()` DEPOIS que o próprio jogo já
carregou aquela tela pelo menos uma vez na sessão — não é garantido, é
"provável" dependendo do que o jogador já visitou.

**Mudanças (2026-09-22)**:
- `Overlay/GameUiAssets.cs` (novo) — `GameUiAssets.TryGetSprite(name)`,
  índice `Dictionary<string, Sprite>` construído uma única vez (lazy) via
  `Resources.FindObjectsOfTypeAll<Sprite>()`, cacheado pro resto da sessão.
  Retorna `null` sem exceção se o nome não resolver (sprite ainda não
  carregado pelo jogo, ou nome errado) — nunca quebra a GUI.
- `Overlay/TranslationEditorOverlay.UIHelpers.cs`, função `Img()` — ÚNICO
  ponto de mudança que reskina a mod inteira: mapeia cada cor de fundo já
  usada (`Bg`→"9 Slice Main Window Background Round", `BgHdr`→"9 Slice
  Square Window BG grey", `BgBtn`→"AcceptButton", `BgClose`→"9 Sliced Red
  Button", `BgChip`/`BgChipOn`/`BgIgnBtn`→"9-slice-simple-button") pra um
  sprite real, aplicado como `Image.Type.Sliced` com o TINT de cor
  original preservado por cima (mesma linguagem de cor de sempre —
  verde=confirmar, vermelho=fechar — só que com textura de verdade). Como
  `HRow`/`Btn`/`ChipBtn`/toda janela/modal já passam por essa MESMA
  função, a mudança se propaga pra literalmente todo o overlay (janela
  principal, Scan, tabelas de arquivo, Move/Copy, Criar pasta/arquivo,
  Detalhe, Auto Tradução) sem editar nenhum desses arquivos individualmente.
  Cores de linha de lista (`BgRow`/`BgRowAlt`/`BgIgnoredRow`) e de campo de
  texto (`BgInput`) foram DELIBERADAMENTE deixadas de fora do mapa —
  9-slice não faz sentido pra uma faixa fina de linha de tabela, e mexer no
  visual do InputField foi evitado por ser uma área já documentada como
  frágil (`project_unity_inputfield_activate_selection_quirk` na memória).
- Log de diagnóstico de uma linha (`LogSpriteResolutionOnce()`, chamado
  uma vez no fim de `BuildUI()`) — não é necessário pra usar a GUI, só
  ajuda a distinguir "sprite ainda não carregado nesta sessão" de "nome
  errado" caso algo apareça sem moldura, sem precisar de mais uma rodada
  de investigação.
- Fonte **NÃO** foi trocada nesta passada — os nomes de fonte encontrados
  ("TrajanPro3-Regular SDF Atlas" etc.) são especificamente ativos TMP
  (SDF), que só funcionam com `TMP_Text`/`TMP_InputField`, não com o
  `UnityEngine.UI.Text`/`InputField` legado que o overlay inteiro usa hoje.
  Migrar pra TMP tocaria dezenas de arquivos (todo `EntryRow`, `Detail`,
  `AutoTranslate`, etc.) — decidido tratar como uma etapa separada e maior,
  só se o reskin de sprite sozinho não for suficiente pro usuário.

**Riscos e mitigação**: risco de regressão é baixo por design — `Img()`
sempre define `sprite`/`type`/`color` nos dois ramos (mapeado ou não), e
o fallback pro comportamento antigo (retângulo de cor lisa) é exatamente
igual ao código anterior. Nenhuma cor de linha/input foi tocada. Build
limpo, 163/163 testes (a reskin em si não é testável por xUnit — é 100%
visual/Unity, mesma limitação documentada desde a Etapa 1 do revamp de
UI). Backup pré-mudança: `_backup_20260922_185038`; pós-mudança:
`_backup_20260922_185334`.

**Pendente de validação em jogo (2026-09-22)** — não dá pra confirmar sem
rodar o jogo: abrir F10 e ver se as janelas/cabeçalhos/botões aparecem com
moldura de verdade (não mais retângulo liso). Se algum sprite específico
não aparecer, é esperado que seja porque o jogo ainda não carregou aquela
tela nesta sessão (não um bug) — o log `[AQWTranslation][Reskin]` no
`BepInEx/LogOutput.log` diz exatamente quais dos 5 sprites resolveram.

**Não fazer ainda**: migração de fonte pra TMP (ver acima), reskin de
linhas de lista/inputs, animações/transições.

**Rounds de ajuste após validação real em jogo (2026-09-22)**:

- **Round 1** — usuário reportou tudo ainda liso após restart. Diagnóstico
  (log lido diretamente, sem pedir nada ao usuário): `Debug.Log` não
  aparece no `BepInEx/LogOutput.log` neste setup — trocado por `Plugin.Log`
  (canal já usado por `TranslationLogger`), com `using AQWMod.Localization;`
  novo em `UIHelpers.cs`.
- **Round 2** — com o log correto, `0/5` sprites resolvidos e `0` sprites
  de UI de qualquer nome em memória. Causa raiz real (não os nomes):
  `BuildUI()` rodava em `Awake()` — no boot do processo, antes de
  QUALQUER tela real do jogo carregar, então `GameUiAssets` indexava
  "praticamente nada" e ficava PERMANENTEMENTE preso nisso (nada revisita
  os `Image` já construídos depois). Corrigido: `BuildUI()` adiado pro
  primeiro F10 (`EnsureUiBuilt()`, chamado só no branch do F10 em
  `Update()`); `GameUiAssets.Invalidate()` (novo) reindexação sob demanda
  a cada reabertura do F10 (`SetVisible(true)`) — ajuda pelo menos as
  linhas de lista, reconstruídas a cada refresh; cabeçalho/painéis
  (construídos uma única vez) só se beneficiam se o primeiro F10 já
  acontecer com bastante coisa carregada. `Update()` teve que ganhar um
  guard `if (_window != null)` em volta de tudo que dependia da GUI já
  existir (F11/inspect/caret do detail) — a CAPTURA CONTÍNUA
  (`ScanVisibleIntoRegistry`) e o auto-refresh do Scan window ficaram
  DELIBERADAMENTE fora desse guard (não referenciam `_window`, têm que
  continuar rodando mesmo antes do primeiro F10 — regra já estabelecida:
  "o polling permanente pode seguir").
- **Round 3** — funcionou parcialmente: "Fechar"/"Del" (vermelho,
  `BgClose`) ficaram estilizados, confirmando o mecanismo. Faltava a
  borda dourada nos botões verde/chip. Causa: `AcceptButton`/
  "9-slice-simple-button" (os 2 nomes chutados originalmente) não
  existem — o log real (983 sprites em memória) revelou os nomes
  verdadeiros: **"Primary Button Fill"** (preenchimento tingível) +
  **"Primary Button Frame"** (moldura dourada, camada separada). Trocado
  `BgBtn`/`BgChip`/`BgChipOn`/`BgIgnBtn` pra usar "Primary Button Fill",
  e `Btn()` ganhou uma segunda camada de `Image` (moldura, sem tint,
  `raycastTarget=false`) por cima, só pra essas 4 cores (não em
  `BgClose`, que já é autocontido).

**Feedback do usuário após o Round 3 (2026-09-22) — ajustes finos e
limpeza de UI, tudo na mesma passada**:
- Borda da moldura ficou grossa demais e sem o `radius` do preenchimento
  por baixo (cantos quadrados vazando da moldura arredondada) — corrigido
  com `Image.pixelsPerUnitMultiplier` (constante `ButtonSpritePpuMultiplier
  = 2.5f`, ajustável) pra afinar a borda, e reestruturando `Btn()` pra ter
  o preenchimento como um filho SEPARADO recuado ~3px da moldura (evita o
  vazamento de quina quadrada). **Efeito colateral achado e corrigido no
  processo**: `HRow()` usa `childForceExpandHeight=true`, que estica
  QUALQUER filho pra caber na altura fixa da linha — ou seja, aumentar só
  a altura mínima do botão (`MinButtonHeight=28`) não tinha efeito nenhum
  sem TAMBÉM aumentar a altura mínima da própria linha (`HRow` ganhou o
  mesmo piso).
- Texto auxiliar da árvore ("Clique numa pasta...") e a barra de
  path/info removidos inteiramente (`_pathLabel` e sua linha inteira) —
  também eram o único uso restante do botão "i" por linha, removido junto.
- Inspetor de Elementos (F11) removido do mod — extraído pra um pacote
  standalone (`AQWElementInspector.zip`, entregue ao usuário) com código
  genérico (sem depender de `TMPTextPatch`/`TranslationManager`) + um
  `README.md` explicando como compilar como mod BepInEx próprio e como
  reintroduzir consciência de tradução se quiser depois. `ViewMode.Inspect`
  saiu do enum.
- Botão "Bib." (Biblioteca) removido da tela — árvore+tabela de arquivo
  já cobrem o mesmo caso de uso. `ViewMode.Library` saiu do enum;
  `DoLibrary`/`ToggleLibrary`/`BuildLibraryRowsIncremental` removidos;
  `Overlay/LibraryEntryListBuilder.cs` e seus testes apagados (único
  chamador era `DoLibrary`). Como consequência direta (nada mais no
  overlay usa `_mainPanel`/linhas de tradução na janela principal —
  Árvore e Config não usam linha nenhuma), o RODAPÉ inteiro da janela
  principal (Salvar/Marcar/Desmarcar/Auto Traduzir/Mover-Copiar) e o
  toggle "[ ] Sem trad." também saíram, junto com `_mainPanel` em si.
- "Buscar" da janela principal agora filtra ARQUIVOS/PASTAS da árvore por
  nome (case-insensitive, mantém pastas-ancestrais de um resultado
  profundo visíveis) em vez de filtrar linha de tradução (que a árvore
  nunca teve) — `FileTreeBuilder.Filter` novo (puro, testado).
- Coluna "St." (status `[]`/`[~]`/`[T]`) removida dos cabeçalhos (janela
  principal, Scan, tabelas de arquivo) e da linha (`BuildRow`) — a
  cor do texto original já comunica o mesmo status (verde=traduzido,
  roxo=ignorado, amarelo=faltando), então a informação não se perdeu,
  só o selo de texto redundante.
- Checkbox de seleção (linhas de tradução, inclusive "Textos detectados
  na tela") trocado de texto `"[x]"`/`"[ ]"` pra um glifo `"✓"`/vazio —
  combinado com o preenchimento+moldura dourada que os botões já ganham
  automaticamente (mesma cor `BgChip`/`BgChipOn`), fica visualmente um
  botão de marcação de verdade.
- Filtro de categoria (chips "Cat:") removido da janela "Textos
  detectados na tela" — não estava sendo útil. Como ficou sem nenhum
  consumidor (Biblioteca também saiu), `RebuildCategoryChips`/
  `UpdateChipColors`/`ChipBtn`/`BuildChipsRow` e os campos
  `ChipsContent`/`ImgTodas`/`CategoryChipImgs` de `RowListPanel` foram
  removidos por inteiro.
- **Bug real pego durante a limpeza**: `EntryRow.SelImg`/
  `AutoPreviewRow.CheckImg` agora apontam pro preenchimento ("Fill"),
  filho do botão, não mais o botão em si — código que fazia
  `SelImg.GetComponentInChildren<Text>()` esperando achar o rótulo de
  texto (que é IRMÃO de Fill, não filho) silenciosamente pararia de
  funcionar. Corrigido subindo pro pai antes de buscar
  (`SelImg.transform.parent.GetComponentInChildren<Text>()`) nos dois
  lugares afetados (`SetRowSelected`, preview do Auto Traduzir).
- **2 campos comprovadamente mortos pelo compilador** (mesmo padrão de
  detecção via warning já usado nas Etapas 2/3 do revamp estrutural):
  `_imgLibrary` e `_autoTranslateBtnText` ficaram sem nenhum escritor
  depois das remoções acima — CS0649 confirmou, removidos por completo
  (não só a atribuição — os `if (!= null)` que os liam também).

Build: 0 erros, 0 avisos (voltou à baseline limpa). Testes: 163/163 (a
remoção de `LibraryEntryListBuilderTests.cs`, ~6 testes, foi compensada
pelos 6 novos testes de `FileTreeBuilder.Filter`). Backups: pré-lote
`_backup_20260922_192924`; pós-lote `_backup_20260922_201843`.

**Pendente de validação em jogo (mais uma vez, 2026-09-22)** — precisa de
reinício completo do jogo (código C# só carrega uma vez por processo, ao
contrário dos JSONs de tradução que têm hot reload de verdade): conferir
se a borda dourada agora tem espessura/cantos corretos; se os botões
parecem menos "finos"; se a busca da árvore filtra corretamente; se o
checkbox "✓" aparece certo; se nada quebrou nas telas que sobraram
(Árvore, Scan, tabelas de arquivo, Config).
