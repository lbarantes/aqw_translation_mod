# TECHNICAL_DEBT.md — Problemas identificados no sistema atual

Cada item foi confirmado lendo o código (arquivo:linha aproximada), não
suposto. Classificação por **impacto** (o que quebra/custa se não for
corrigido), **dificuldade** (esforço de corrigir) e **dependências** (o que
precisa existir antes). Sem pontuação numérica arbitrária — os campos abaixo
já dizem o que é preciso saber para priorizar.

---

## 1. `UnityEngine.UI.Text` não é interceptado — só alcançado por polling + reescrita — **corrigido (2026-09-21)**

> **Status**: corrigido. Novo `Interceptors/LegacyTextPatch.cs` adiciona um
> Prefix Harmony em `UnityEngine.UI.Text.text` (setter não-abstrato, mesmo
> padrão já usado em `TMP_Text.text`), delegando para
> `TMPTextPatch.TranslateRaw` e mantendo `LegacyOriginalTracker` atualizado.
> A tradução de UI.Text agora acontece no momento da escrita, sem esperar o
> próximo tick de varredura. **A varredura periódica e o
> "enforcement-on-scan" foram mantidos deliberadamente** — ver nota de
> revisão em `TECHNICAL_DEBT.md` #5, `ARCHITECTURE.md` §6 e `ADR-0004`.

**Onde**: não existe nenhum patch Harmony para `UnityEngine.UI.Text` em
`Interceptors/`. A única cobertura é `ScanVisibleIntoRegistry` +
"enforcement-on-scan" em `Overlay/TranslationEditorOverlay.cs:443-499`.

**Impacto**: alto. Diálogos/popups legados (`"Goto map..."`, falas de NPC em
UI.Text) ficam em inglês por até ~1s após aparecerem, e **só** são
traduzidos se `EnforceOnScan=true` (é o default, mas é uma flag que pode ser
desligada por "causar efeitos colaterais" — o próprio comentário do config
admite isso). Se o jogo reescreve o mesmo `Text` todo frame (loop de UI comum
em HUDs dinâmicos), o mod fica "brigando" com o jogo: escreve tradução,
jogo sobrescreve com o original no próximo frame, mod escreve de novo no
próximo tick de 1s — resultando em piscar visível.

**Dificuldade**: baixa-média. `UnityEngine.UI.Text.text` é um setter
**não-abstrato** (ao contrário de `TMP_Text`), então o mesmo padrão de Prefix
usado com sucesso em `TMP_Text.text` deve funcionar diretamente.

**Dependências**: nenhuma — pode ser feito antes de qualquer refactor de
storage.

---

## 2. Identidade da tradução = só o texto normalizado (sem desambiguação) — **corrigido (2026-09-21)**

> **Status**: implementado por completo. Além da infraestrutura
> (`Core/TranslationKey.cs`, `TranslationRepository.TryGet(key, context,
> out value)`, `LookupStage` propagando `ctx.Context`), `TMPTextPatch`
> agora INFERE automaticamente um `context` a partir de `GetCategory`
> (o mesmo agrupamento já usado pelos chips de categoria da GUI — não uma
> heurística nova) e o passa em toda chamada a `TranslationManager.Translate`
> (`DoTranslate` e `TranslateRaw`). Uma entrada pode ser definida como
> `{"text":"Ataque","context":"SkillTree"}` e passa a ser alcançada de
> verdade quando o texto aparecer sob aquele agrupamento de hierarquia.
> **Por que isto é seguro mesmo sem poder testar contra o jogo real**: a
> busca por `texto@@contexto` SEMPRE cai de volta para `texto` puro se não
> houver entrada qualificada — e nenhuma existe ainda no corpus real. Logo,
> hoje, 100% das traduções continuam resolvendo exatamente como antes (a
> tentativa de contexto simplesmente não encontra nada e cai no fallback);
> a mudança só passa a ter efeito no dia em que alguém autorar uma entrada
> com `context` correspondente.

**Onde**: `LookupStage.Process` (`Pipeline/Stages/LookupStage.cs`) busca
apenas pela string; `TranslationRepository.LoadFile`
(`Repository/TranslationRepository.cs:157-198`) funde tudo num único
`Dictionary<string,string>` chaveado só pelo texto.

**Impacto**: alto, mas silencioso — não quebra nada visivelmente hoje porque
o corpus é pequeno e provavelmente não tem colisões reais ainda. É uma bomba-
relógio: o mesmo texto em contextos diferentes (skill "Attack" vs. botão
"Attack") **sempre** recebe a mesma tradução, sem aviso, sem override por
contexto. É um risco real, não
hipotético.

**Dificuldade**: média. Resolvido por um mecanismo de contexto **opcional**
aditivo (ver `ADR-0001`), não por reescrever a identidade para
hierarquia/GameObject (que traria problemas piores — ver ADR).

**Dependências**: nenhuma para o design; a implementação se encaixa melhor
junto com o novo storage (`ADR-0002`).

---

## 3. Merge de arquivos é last-write-wins silencioso e não-determinístico — **corrigido (2026-09-21)**

> **Status**: corrigido. `TranslationRepository.LoadAll` agora ordena os
> arquivos alfabeticamente pelo caminho relativo (determinístico, não a
> ordem do SO), separa explicitamente a camada `overrides/` (sempre vence)
> da camada de conteúdo, e loga um `WARNING` quando dois arquivos da mesma
> camada definem valores diferentes para a mesma chave (em vez de
> sobrescrever silenciosamente). `translations/ingame_edits.json` na raiz
> continua sendo tratado como alias da camada `overrides/`, então **nunca
> mais perde** para um arquivo de conteúdo carregado depois.

**Onde**: `TranslationRepository.LoadAll` (`Repository/TranslationRepository.cs:73-107`),
`target[original] = translated` sem checar se já existia com valor diferente.
A ordem vem de `Directory.GetFiles(..., SearchOption.AllDirectories)`, que
não garante ordem alfabética nem estável entre SOs.

**Impacto**: alto. `ingame_edits.json` (as edições manuais mais recentes do
tradutor) pode **perder silenciosamente** para outro arquivo carregado depois,
dependendo da ordem de enumeração do sistema de arquivos — sem log, sem
aviso. Isso é exatamente o tipo de bug que "funciona na minha máquina" e falha
de forma inconsistente para outro usuário/SO.

**Dificuldade**: baixa. Precisa de (a) ordem de carregamento explícita por
prioridade de camada e (b) log de `WARNING` quando
duas fontes definem valores diferentes para a mesma chave.

**Dependências**: nenhuma — pode ser corrigido isoladamente, mas o design
correto de prioridades faz mais sentido junto do novo storage.

---

## 4. Normalização de texto duplicada em dois lugares que não se comunicam — **corrigido parcialmente, por decisão consciente (2026-09-21)**

> **Status**: a parte que era REALMENTE duplicação de implementação foi
> corrigida — `TMPTextPatch.StripRichTextTags` agora delega para
> `Utils/RichTextUtils.StripAll` (mesma assinatura, mesmo comportamento
> observável, algoritmo interno agora reconhece tags TMP de verdade via
> regex em vez de tratar qualquer `"<...>"` como tag). Validado contra
> **todo** o corpus real do usuário (`BepInEx/plugins/translations/`, 2938
> ocorrências de tag em 8 arquivos): a contagem de "qualquer `<tag>`" e a
> de "tag reconhecida pela lista do RichTextUtils" bateram exatamente —
> zero tags reais ficariam sem strip com a troca.
>
> **O que NÃO foi consolidado, por decisão deliberada**: a normalização de
> `{player}`/`{n}`/`{map}`/`{npc}` e o template de quest continuam em
> `TMPTextPatch` — essa lógica não está duplicada em lugar nenhum (é a
> única implementação que existe), e o fluxo de `DoTranslate` tem
> ramificações (template de quest antes da cadeia geral, fallback de NPC
> só quando a busca primária falha) que não cabem no modelo linear de
> estágios do pipeline sem inventar uma arquitetura nova, não testada
> contra o jogo real. Migrar isso para o pipeline continua sendo uma opção
> futura, não descartada — só não foi feita agora por ter risco alto e
> retorno menor do que a parte que foi corrigida.

**Onde**: `TMPTextPatch.DoTranslate`/`TranslateRaw` fazem strip de rich text
e normalização de `{player}/{n}/{map}/{npc}` manualmente
(`Interceptors/TMPTextPatch.cs`, ~400 linhas de lógica). Separadamente,
`Pipeline/Stages/RichTextStage.cs` e `Pipeline/Stages/PlaceholderStage.cs`
fazem uma versão paralela (strip/reinject de tags TMP; conversão de
`%nome%`/`[[nome]]` para `{nome}`) dentro do `LocalizationPipeline`, que roda
**depois** — no caminho real (via `TMPTextPatch`), sobre um texto que já foi
processado manualmente, tornando esses estágios do pipeline no-ops na
prática.

**Impacto**: médio. Não causa bug hoje (o pipeline dentro do
`TranslationManager` é idempotente sobre texto já limpo), mas é uma fonte
real de confusão para quem for manter o código: duas implementações de
"proteja isto antes de traduzir" que evoluem de forma independente e podem
divergir silenciosamente (ex.: um novo tipo de tag TMP adicionado em um lugar
e esquecido no outro).

**Dificuldade**: média. Exige decidir qual das duas é a fonte de verdade
(recomendação: consolidar em `Pipeline/Stages`, e fazer `TMPTextPatch`
alimentar o pipeline com o texto **bruto**, delegando toda a proteção de
tags/placeholders a estágios — ver `ARCHITECTURE.md`).

**Dependências**: nenhuma, mas é arriscado mexer sem testes de regressão
manuais cobrindo os casos hoje tratados (username, números, mapas, NPCs,
template de quest) — recomend­a-se fazer isso **depois** de ter uma suíte
mínima de casos de teste manuais/golden files.

---

## 5. Varredura em polling permanente (1Hz), independente do overlay estar aberto — **mantida por decisão do usuário (2026-09-21)**

> **Status**: revisado, não corrigido — e não será, por ora. O usuário
> confirmou explicitamente que quer manter esta varredura: é o mecanismo
> que atualiza a lista de traduções do overlay sem exigir clique manual em
> "Refresh", e o custo não é perceptível na prática de uso real. A proposta
> original deste documento (substituir o timer perpétuo por
> `SceneManager.activeSceneChanged` + refresh manual) fica descartada. O
> que **foi** feito (item #1 acima) resolve a parte que de fato importava
> para a correção da tradução — `UI.Text` agora traduz no momento da
> escrita, não depende mais desta varredura para isso. A varredura
> continua existindo só para popular a lista da GUI e como rede de
> segurança para texto servido por serialização direta do Unity.

**Onde**: `TranslationEditorOverlay.Update()` chama `ScanVisibleIntoRegistry()`
a cada 1s **sempre**, e o botão "⟳ Auto" adiciona uma segunda varredura
completa (`CollectVisibleTextSet`) quando ligado.

**Impacto**: médio (perf) hoje, mas cresce com a complexidade de cena do
jogo — `FindObjectsByType<TMP_Text>`/`<Text>` percorre **todos** os objetos
carregados, ativos e inativos, todo segundo, para sempre, mesmo com o
overlay fechado e mesmo que o jogador não esteja traduzindo. É exatamente o
"modo automático de leitura da tela", que vale reconsiderar.

**Dificuldade**: média. Ver `ADR-0004` — a resolução correta combina (a)
patchear `UI.Text` (item 1, elimina a necessidade de reescrita por varredura),
(b) usar `SceneManager.activeSceneChanged` (evento real do Unity) para
disparar 1 varredura por troca de cena em vez de um timer perpétuo, e (c)
remover o "⟳ Auto" (a comparação de conjunto completo por frame), mantendo
só o gatilho barato já existente (`CaptureRegistry.Count` mudou).

**Dependências**: item 1 (patch de UI.Text) deveria vir primeiro para não
perder cobertura ao reduzir a varredura.

---

## 6. `_scan_debug.txt` é reescrito a cada tick de varredura — **corrigido (2026-09-21)**

> **Status**: corrigido. O bloco de escrita agora só roda quando
> `VerboseLogging=true` (`Overlay/TranslationEditorOverlay.cs`, dentro de
> `ScanVisibleIntoRegistry`).

**Onde**: `Overlay/TranslationEditorOverlay.cs:501-517`.

**Impacto**: baixo. I/O de disco síncrono no main thread a cada ~1s
indefinidamente, mesmo com o overlay fechado. Pequeno, mas gratuito — em
HDDs mecânicos ou storage de rede isso pode ser perceptível.

**Dificuldade**: trivial (gate por `VerboseLogging` ou por overlay visível).

**Dependências**: nenhuma.

---

## 7. Hot reload de um arquivo custa O(total de traduções), não O(arquivo alterado) — **corrigido (2026-09-21)**

> **Status**: corrigido. `_translations`/`_sourceFileMap`/`_keysByFileMap`
> agora são `ConcurrentDictionary` em vez do padrão anterior de "copiar
> tudo + trocar referência". `ReloadFile` atualiza só as chaves do arquivo
> alterado — O(chaves do arquivo), não O(total do corpus). `LoadAll`
> (carga inicial, única vez, sem leitor concorrente possível) continua
> montando os dados em `Dictionary<>` comuns durante o merge de camadas
> (lógica de conflito inalterada) e só popula as estruturas concorrentes
> ao final. `ConcurrentDictionary` já era um padrão usado em outros lugares
> do projeto (`ComponentPathCache`, `MissingTranslationCollector`), não é
> uma dependência nova.

**Onde (antes da correção)**: `TranslationRepository.ReloadFile`
copiava o dicionário **inteiro** (`new Dictionary<>(_translations, ...)`) antes
de aplicar o delta do arquivo alterado.

**Impacto**: baixo hoje (corpus pequeno), mas escala mal — se o corpus
crescer para dezenas de milhares de entradas (o objetivo declarado do
projeto, cobrindo saga inteira + classes + mapas), cada save na GUI (que
sempre dispara reload) fica proporcionalmente mais lento.

**Dificuldade**: baixa — é uma otimização isolada, não estrutural.

**Dependências**: nenhuma; vale medir antes de otimizar (corpus atual é
pequeno o bastante para não importar ainda).

---

## 8. Escrita de `ingame_edits.json` não é atômica e não tem backup — **corrigido (2026-09-21)**

> **Status**: corrigido. `WriteFile` agora grava em `ingame_edits.json.tmp`
> e usa `File.Replace` (rename atômico + mantém `ingame_edits.json.bak` da
> versão anterior) em vez de `File.WriteAllText` direto no arquivo final.

**Onde**: `Overlay/TranslationEditorOverlay.cs: WriteFile` (~1415-1434) —
`File.WriteAllText` direto no arquivo final, sem escrever em um temporário e
renomear, sem cópia de segurança anterior.

**Impacto**: médio. Uma queda de energia, crash do jogo, ou exceção no meio
da serialização pode truncar/corromper o único arquivo onde ficam as edições
manuais mais recentes do tradutor — potencialmente **a perda de trabalho
mais dolorosa possível** no fluxo atual (justamente o que a regra
"nunca perder dados" quer evitar).

**Dificuldade**: baixa. Escrever em `ingame_edits.json.tmp`, então
`File.Replace`/`Move` atômico, mantendo a versão anterior como `.bak`.

**Dependências**: nenhuma — deveria ser uma correção isolada e imediata,
independente do resto do roadmap.

---

## 9. Bloqueio de mouse não cobre o painel de detalhe — **corrigido (2026-09-21)**

> **Status**: corrigido. `InputBlockerPatch.MouseOverOverlay` agora também
> testa uma lista `ExtraBlockedPanels`, na qual `BuildDetailPanel` registra
> o `RectTransform` do modal de detalhe.

**Onde**: `InputBlockerPatch.MouseOverOverlay()` testa só contra
`OverlayWindow` (a janela principal). O painel `_detailPanel`
(`Overlay/TranslationEditorOverlay.cs:742` em diante) é um `GameObject`
irmão, fora do `RectTransform` da janela principal.

**Impacto**: médio (UX/segurança de input). Um clique sobre o modal de
detalhe (fora da área da janela principal) pode **vazar** para o jogo por
baixo — exatamente a classe de bug a
evitar ("clique acidental no jogo").

**Dificuldade**: trivial — incluir o rect do `_detailPanel` na checagem
(`MouseOverOverlay` testando múltiplos retângulos, ou um retângulo
envolvente calculado dinamicamente).

**Dependências**: nenhuma.

---

## 10. Entradas "ignoradas" são gravadas como identidade (`"X":"X"`) — **corrigido também no Scan (2026-09-21)**

> **Status**: corrigido por completo. `IgnoreEntry` foi unificada — Scan
> (`ingame_edits.json`) e Biblioteca (qualquer arquivo) agora usam o mesmo
> caminho (`TranslationFileStore.Upsert(..., ignore: true)`), gravando o
> campo explícito em vez da convenção antiga `dict[key] = key` em qualquer
> um dos dois. `SaveEntry` (botão "OK") continua gravando string simples
> mesmo quando o texto digitado coincide com o original — de propósito,
> para não confundir uma tradução real que por acaso é igual à fonte
> (ex.: "OK"→"OK") com "ignorada". O repositório continua interpretando a
> convenção antiga corretamente para arquivos já existentes com esse
> formato (retrocompatível).

**Onde (antes da correção)**: `IgnoreEntry` (`Overlay/TranslationEditorOverlay.cs`)
gravava `dict[key] = key` para representar "não precisa traduzir" quando a
linha vinha do Scan (`ingame_edits.json`).

**Impacto**: médio. Não há como distinguir programaticamente "ignorado de
propósito" de "coincidentemente igual" (ex.: `"OK"` → `"OK"` pode ser uma
tradução real). Também infla o arquivo e complica qualquer ferramenta futura
que precise responder "quantas strings realmente faltam traduzir".

**Dificuldade**: baixa — adicionar um campo `ignore: true` explícito no novo
schema (ver `TRANSLATION_FORMAT.md`) resolve; migração mantém
retrocompatibilidade interpretando `saved == key` como `ignore: true` ao
importar dados legados.

**Dependências**: novo schema (`ADR-0002`).

---

## 11. "Bib." (Biblioteca) só mostra `ingame_edits.json`, não o corpus completo — **dado pronto, GUI ainda pendente (2026-09-21)**

> **Status**: parcial. `TranslationIndex` (`Core/TranslationIndex.cs`),
> exposto via `ITranslationRepository.Index`, agora mantém proveniência
> arquivo↔chave completa (todos os arquivos, não só `ingame_edits.json`) —
> o dado que a Biblioteca precisaria para navegar o corpus inteiro já
> existe. **A GUI ainda não foi alterada para consumi-lo** — `DoLibrary()`
> continua lendo só `ingame_edits.json`, como antes. Isso é trabalho da
> Fase 4 (`docs/ROADMAP.md`), que agora não precisa mais inventar essa
> infraestrutura, só consumi-la.

**Onde**: `DoLibrary()` (`Overlay/TranslationEditorOverlay.cs:1040-1064`) lê
exclusivamente via `ReadFile()` (que só olha `ingame_edits.json`).

**Impacto**: médio (funcional/UX). Traduções que já vivem em arquivos
curados (`ui/hud.json`, futuros `classes/Warrior.json` etc.) **não aparecem**
na Biblioteca — o usuário não consegue navegar/editar/buscar o corpus
completo pela GUI, só as edições ainda não "promovidas". Contradiz
o propósito da Biblioteca.

**Dificuldade**: média — depende de expor uma forma de leitura agregada
por arquivo/pasta a partir do `TranslationRepository` (hoje ele só expõe um
dicionário achatado, sem saber de qual arquivo cada chave veio).

**Dependências**: o repositório precisa manter a proveniência arquivo→chave
(hoje só mantém `categoryIndex` por nome de arquivo, sem caminho completo) —
isso é parte do trabalho de `ADR-0002`.

---

## 12. GUI é um monólito sem separação de camadas — **suíte de testes + divisão completa do arquivo (2026-09-21/22)**

> **Status final (2026-09-22)**: `Overlay/TranslationEditorOverlay.cs`
> (chegou a ~2850 linhas) foi dividido em 12 arquivos via **partial class**
> do C# — mesma classe compilada, mesmos campos, zero mudança de
> comportamento (verificado por auditoria de cobertura de linha: as faixas
> extraídas cobrem 100% do arquivo original sem sobreposição nem perda,
> fora uma linha em branco decorativa) e por build limpo na primeira
> tentativa:
> - `TranslationEditorOverlay.cs` — núcleo: tipos internos, campos, ciclo
>   de vida do MonoBehaviour (~360 linhas).
> - `.Scan.cs` — varredura de TMP_Text/UI.Text + modo Scan (~470 linhas).
> - `.BuildUI.cs` — construção da janela principal (~150 linhas).
> - `.Detail.cs` — modal de detalhe/editor avançado (~330 linhas).
> - `.AutoTranslate.cs` — orquestração/preview/aplicação do Auto Traduzir
>   em lote (~350 linhas).
> - `.MoveCopy.cs` — painel Mover/Copiar (~130 linhas).
> - `.Library.cs` — modo Biblioteca (~190 linhas).
> - `.Settings.cs` — aba Configurações (~110 linhas).
> - `.Rows.cs` — construção de linha, chips de categoria, Salvar/Ignorar
>   (~275 linhas).
> - `.JsonIO.cs` — leitura/escrita de `ingame_edits.json` (~60 linhas).
> - `.UIHelpers.cs` — helpers genéricos de construção de UI (~220 linhas).
> - `.Inspector.cs` — modo Inspetor/F11 (~375 linhas).
>
> **Isto NÃO é** a separação ViewModel/Renderer completa desenhada em
> `GUI_ARCHITECTURE.md` §3 (que exigiria desacoplar `EntryRow` — hoje dado
> + referências Unity no mesmo objeto — numa camada de dados pura + uma
> camada de view sincronizada, um redesenho real do fluxo de dados da
> lista). Essa versão mais ambiciosa foi avaliada e descartada
> deliberadamente: exigiria reescrever como as linhas são criadas/
> atualizadas, sem nenhuma forma de testar automaticamente o resultado
> (não há Unity Editor/Player neste ambiente), e o ganho estrutural sobre
> "arquivo dividido por assunto" seria marginal para o risco de regressão
> silenciosa numa GUI que já funciona e foi testada manualmente por várias
> sessões. A decisão de seguir com a versão mecânica (partial class) em
> vez da reescrita foi tomada explicitamente pelo usuário depois de expor
> esse trade-off.
>
> Toda a lógica de negócio extraível (filtro, seleção, status de linha,
> validação de caminho, ordenação, montagem de lista da Biblioteca,
> planejamento do preview de Auto Tradução, formatação de dica de
> placeholder) já tinha sido isolada em classes próprias e testadas
> ANTES desta divisão (ver histórico abaixo) — a divisão em arquivo cobre
> exatamente o que sobrou: construção de UI do Unity, que continua
> dependendo de teste manual em jogo (não há como automatizar sem um
> Unity Editor/Player).

**Histórico da extração de lógica (2026-09-21):**

> **Status**: parcial, de propósito, agora com uma suíte de testes
> automatizados real por trás (`Tests/translation_mod.Tests.csproj`, xUnit,
> 104 testes, `dotnet test` a partir de `Tests/`). Cobre toda a lógica que
> NÃO depende de um Unity Editor/Player de verdade: `TranslationRepository`
> (merge de camadas, conflitos, hot reload), `TranslationFileStore`
> (leitura/escrita atômica), `TranslationKey`, `RichTextUtils`, as funções
> de normalização de placeholders de `TMPTextPatch` (números/username/
> NPC/mapa), `AutoTranslationOrchestrator` (via um `ITranslationProvider`
> falso — sem bater na API real), `EntryRowFilter`, `SelectionState`,
> `RowStatus` e `DestinationPathValidator`. **NÃO cobre** — e não tem como
> cobrir sem um Unity Editor/Player rodando — a construção de UI em si
> (GameObject/Button/InputField/Text, `Overlay/TranslationEditorOverlay.cs`
> continua exigindo teste manual em jogo para essa parte).
>
> Achado de bônus ao escrever os testes: `TMPTextPatch.NormalizeMaps` tinha
> um bug real (`i = wordEnd` em vez de `i = dash + 1` após processar um
> match) que causava `ArgumentOutOfRangeException` para textos no padrão
> "N jogadores em mapa-N" — nunca percebido pelo usuário porque
> `DoTranslate`/`TranslateRaw` têm um `catch(Exception)` amplo que engolia
> o erro e devolvia o texto original sem traduzir (silêncio, não crash).
> Corrigido junto.
>
> Fatias extraídas até agora, todas sem mudança de comportamento visível
> (fora do bug acima) e agora com teste unitário cada — confirmadas em
> jogo pelo usuário após a primeira leva (RowStatus/DestinationPathValidator):
> - `Overlay/EntryRowFilter.cs` — filtro da lista (busca + categoria +
>   "sem tradução").
> - `Overlay/SelectionState.cs` — quais linhas estão marcadas para
>   Auto Traduzir/Mover-Copiar.
> - `Overlay/RowStatus.cs` — decide traduzido/ignorado/faltando a partir
>   de original+salvo (estava duplicado em `BuildRow` e `ApplyRowStatus`
>   com nomes de variável ligeiramente diferentes).
> - `Overlay/DestinationPathValidator.cs` — validação do campo de destino
>   do painel Mover/Copiar (proteção contra path traversal).
> - `Overlay/EntryOrdering.cs` — prioridade de exibição (sem tradução >
>   ignorado > traduzido, alfabético dentro do grupo) — estava duplicada
>   ao pé da letra em `DoLibrary` e `PopulateRows`.
> - `Overlay/LibraryEntryListBuilder.cs` — monta a lista ordenada de
>   entradas da Biblioteca a partir de `ITranslationRepository` (puro
>   dado, sem criar GameObject) — extraído de `DoLibrary`.
> - `Overlay/AutoPreviewPlanner.cs` — decide quais resultados do Auto
>   Traduzir vêm pré-marcados no preview (nunca suspeitos; nunca quando já
>   existe tradução diferente salva) e agrega os contadores prontas/
>   suspeitas/falharam — extraído de `ShowAutoPreview`.
> - `Overlay/PlaceholderHintFormatter.cs` — monta o texto de dica mostrando
>   os valores reais por trás de cada placeholder ({player}/{npc}/{n}/
>   {map}) no path de uma linha do Scan — extraído de
>   `AppendPlaceholderHints`.
> - `Overlay/CorpusFileResolver.cs` — encontra o arquivo real onde uma
>   chave está definida, via `TranslationIndex` — extraído ao corrigir um
>   bug real do botão "Del" (ver item novo abaixo, #19).
>
> **Deliberadamente ainda não foi feita** a extração completa
> (`TranslationBrowserViewModel`/`LibraryViewModel`/`SettingsViewModel`
> cobrindo o arquivo inteiro, ~2850 linhas) — decisão do usuário
> (2026-09-21): continuar em fatias grandes e verificáveis, cada uma
> testada e confirmada em jogo antes da próxima, em vez de um único
> refactor gigante de uma vez.

**Onde (antes desta correção)**: `Overlay/TranslationEditorOverlay.cs` — uma única classe de 1764
linhas concentrando construção de UI, estado, I/O de arquivo, varredura, e
lógica de inspector.

**Impacto**: médio (manutenção/extensibilidade), não funcional. Qualquer
mudança (nova aba de Settings, reskin visual, adicionar Auto Tradução) tem
que ser feita no meio de um arquivo gigante com alto acoplamento entre
UI e lógica.

**Dificuldade**: alta (é um refactor real, não uma correção pontual).

**Dependências**: deveria vir **depois** do novo storage (para não refatorar
a GUI duas vezes) — ver `ROADMAP.md` Fase 3.

---

## 13. Índice de categoria (`categoryIndex`) é código morto — **corrigido (2026-09-21)**

> **Status**: corrigido. `_categoryIndex`/`GetKeysByCategory` foram
> removidos (de `TranslationRepository` e de `ITranslationRepository`) e
> substituídos por `TranslationIndex` (item #11 acima), que tem um
> consumidor real planejado (a Biblioteca da Fase 4) em vez de ficar sem uso.

**Onde**: `TranslationRepository._categoryIndex` e `GetKeysByCategory`
(`Repository/TranslationRepository.cs:143-153`) são populados a cada load,
mas não têm nenhum chamador real no restante do código hoje.

**Impacto**: baixo — não causa bug, só overhead de memória/CPU marginal e
confusão de manutenção (parece que algo consome, mas nada consome).

**Dificuldade**: trivial — remover ou (melhor, dado o roadmap) redesenhar
como parte do índice de proveniência do item 11.

**Dependências**: decidir junto com `ADR-0002` se vira a base do índice novo
ou é removido.

---

## 14. `npc_names.json` não tem hot reload — **corrigido (2026-09-21), mais um bug relacionado achado**

> **Status**: corrigido. `TranslationManager` agora reconhece
> `npc_names.json` no callback de hot reload (`IsNpcNamesFile`) e dispara
> um evento dedicado (`OnNpcNamesFileChanged`) em vez de tratá-lo como
> arquivo de tradução comum — `Plugin.cs` escuta esse evento e chama
> `TMPTextPatch.LoadNpcNames` de novo, mais `InvalidateSafeTranslateCache`.
>
> **Bug relacionado, achado ao investigar isto**: `npc_names.json` estava
> sendo carregado pelo `TranslationRepository` como um arquivo de tradução
> comum (nenhum filtro o excluía), além de ser lido separadamente por
> `TMPTextPatch.LoadNpcNames`. Isso injetava cada par `"Nome do NPC":
> "Nome traduzido"` como se fosse uma tradução de texto solto — ex.:
> `"Booker":"Bibliotecário"` virava uma entrada de tradução real, que
> aplicaria a QUALQUER texto "Booker" isolado que aparecesse em outro
> lugar (não só quando substituindo o placeholder `{npc}`). Corrigido:
> `TranslationRepository.LoadAll` agora exclui explicitamente
> `npc_names.json` da raiz do carregamento como arquivo de tradução.

**Onde (antes da correção)**: `Plugin.Awake` chama
`TMPTextPatch.LoadNpcNames` uma única vez; não havia watcher para esse
arquivo, e `TranslationRepository` não o excluía do carregamento normal.

**Onde**: `Plugin.Awake` chama `TMPTextPatch.LoadNpcNames` uma única vez;
não há watcher para esse arquivo.

**Impacto**: baixo — inconsistência de UX (todo o resto tem hot reload,
esse arquivo específico exige reiniciar o jogo para atualizar).

**Dificuldade**: trivial — reusar `HotReloadWatcher` ou adicionar um watcher
dedicado.

**Dependências**: nenhuma.

---

## 15. Ausência de controle de versão (git)

**Onde**: projeto inteiro — confirmado "Is a git repository: false" no
ambiente, e o único "histórico" existente é a pasta `_backup_20260529_231856/`
copiada manualmente.

**Impacto**: alto, mas indireto — qualquer refactor proposto neste
documento fica muito mais arriscado sem diffs revisáveis e sem capacidade de
reverter cirurgicamente.

**Dificuldade**: trivial (`git init` + primeiro commit).

**Dependências**: nenhuma — deveria ser o **primeiro** passo prático, antes
de qualquer mudança de código, independente de quando o repositório GitHub
remoto for populado.

---

## 16. Entradas "ignoradas"/identidade eram descartadas ao carregar — **descoberto e corrigido durante a Fase 3 (2026-09-21)**

**Onde**: `TranslationRepository.LoadFile` (versão anterior à Fase 3),
`if (translated == original) continue; // Skip identidades`.

**Impacto**: alto na prática, apesar de nunca ter sido percebido
visualmente. Toda entrada gravada como `"X":"X"` (a única forma que a GUI
tinha — e ainda tem — de representar "ignorar", ver item #10) era
**descartada por completo** ao carregar o arquivo, não armazenada como
"traduzido, sem alteração". Consequência real: o `LookupStage` nunca
encontrava essas chaves, `IsTranslated` ficava `false`, e
`TranslationManager.Translate` tratava toda interação com esse texto como
"faltando tradução" — incrementando `_misses`, logando (se `LogMisses`) e
**recapturando indefinidamente no `MissingTranslationCollector`**, gerando
ruído contínuo em `_capture/*.json` para textos que o usuário já havia
explicitamente marcado como "não precisa traduzir". Confirmado contra o
corpus real do usuário (`BepInEx/plugins/translations/ingame_edits.json`,
927 linhas) — várias dezenas de entradas como `"Alpha Test":"Alpha Test"`,
`"LEVEL":"LEVEL"`, `"Linck_":"Linck_"` estavam nessa situação.

**Não afeta o texto exibido no jogo** — o resultado final já era o
original inalterado antes e depois da correção (a única mudança é que a
entrada agora é reconhecida como "resolvida" internamente, em vez de
"faltando").

**Status**: corrigido. `TranslationRepository.LoadFile` agora armazena
identidade/`ignore` como uma entrada real (`TranslationEntry.Text =
original`, `Status = "ignored"`), tanto para a convenção legada
(valor==chave, forma string simples) quanto para o novo campo explícito
`ignore:true` (forma estendida — mas **sem** o fallback de coincidência de
texto nessa forma, para não confundir uma tradução real que por acaso é
igual ao original, ex. `{"text":"OK","ignore":false}`, com uma entrada
ignorada).

**Dificuldade**: baixa (foi corrigido como parte natural da reescrita do
parser para o novo schema — mesma função já estava sendo tocada).

---

## 17. `ReadFile`/`WriteFile` do overlay corrompiam entradas em forma de objeto — **descoberto e corrigido durante a Fase 4 (2026-09-21)**

**Onde**: `Overlay/TranslationEditorOverlay.cs` — `ReadFile()`/`WriteFile()`
(versão anterior à Fase 4) usavam um leitor de tokens
(`Newtonsoft.Json.JsonTextReader`) escrito à mão, que só reconhecia pares
`PropertyName`+`String`. Isso funcionava bem enquanto `ingame_edits.json`
era **sempre** um dicionário achatado de strings — o que era verdade até a
Fase 4 introduzir a Biblioteca completa, que passou a poder gravar a forma
estendida (`{"text":...,"ignore":true}`) em **qualquer** arquivo do corpus,
incluindo `ingame_edits.json`.

**Impacto**: alto (risco de corrupção de dados), descoberto **antes** de
afetar dados reais, durante a implementação da Biblioteca. Se uma entrada
em `ingame_edits.json` virasse forma de objeto (ex.: via "Ignorar" na
Biblioteca) e depois o modo Scan salvasse qualquer OUTRA entrada, o leitor
de tokens antigo:
1. Não reconhecia o valor-objeto como uma unidade — continuava lendo os
   tokens **internos** dele (`"text"`, `"context"`, `"ignore"`) como se
   fossem propriedades de nível superior;
2. Perdia a chave real (`"Attack"`, por exemplo) por completo;
3. Injetava chaves falsas (`"text"`, `"context"`) no dicionário resultante;
4. O próximo `WriteFile()` gravava essa versão corrompida por cima do
   arquivo real, destruindo a entrada original.

**Status**: corrigido antes de ser exposto ao usuário. `ReadFile()` e
`WriteFile()` agora delegam para `Repository/TranslationFileStore.cs`
(baseado em `JObject` de verdade, não em varredura de tokens), que
reconhece corretamente as duas formas e preserva campos estendidos
(`context`/`tags`/`status`) de entradas não tocadas ao regravar o arquivo
inteiro (`TranslationFileStore.WriteAll`, um único read+write por
operação, mesmo perfil de custo de antes — não é uma escrita por chave).

**Dificuldade**: média (exigiu desenhar `WriteAll` para preservar forma
estendida num regrava-tudo-de-uma-vez, não só um upsert por chave).

**Dependências**: `Repository/TranslationFileStore.cs` (Fase 4).

---

## 18. Biblioteca engasgava ao abrir com o corpus real — **achado em teste real e corrigido (2026-09-21)**

**Onde**: `Overlay/TranslationEditorOverlay.cs`, `DoLibrary()`/`PopulateRows()`
(antes desta correção).

**Impacto**: alto (percebido diretamente pelo usuário em teste real: *"Ao
marcar Bib. deu um lagzinho por conta da quantidade de conteúdo"*). Cada
linha da lista principal cria ~19 `GameObject`s (linha + checkbox + status +
original + campo de tradução com 4 sub-objetos + 5 botões, cada um com seu
próprio texto filho). O modo Scan nunca sentiu isso porque sempre foi
limitado a `MaxEntries`/`MaxHistory` (100-400 linhas) — mas a Biblioteca
completa (Fase 4) não tinha limite algum: com o corpus real do usuário
(milhares de entradas somando `ingame_edits.json` + `ui/`, `guis/`,
`screens/` etc.), construir todas as linhas de uma vez, no mesmo frame,
causava um engasgo visível.

**Status**: corrigido. `DoLibrary()` agora só monta a LISTA de entradas
(barato, sem criar nenhum `GameObject`) e delega a construção das linhas de
UI para uma corrotina (`BuildLibraryRowsIncremental`) que cria um lote
pequeno por frame (`LibraryRowsPerFrame = 40`, ajustável) — mesmo princípio
de "orçamento por frame" que `UnityMainThreadDispatcher` já usa em outro
lugar do projeto. Os chips de categoria e os contadores aparecem
imediatamente (não dependem de nenhuma linha estar construída), então dá
para filtrar por categoria enquanto o resto ainda está carregando em segundo
plano.

**Dificuldade**: baixa-média (não muda nenhuma lógica de negócio, só
espalha a construção de UI ao longo de vários frames).

**Dependências**: nenhuma.

---

## 19. Botão "Del" no Scan não removia a entrada quando ela vivia num arquivo curado — **achado pelo usuário e corrigido (2026-09-22)**

**Onde**: `Overlay/TranslationEditorOverlay.Rows.cs`, botão "Del" — linhas
do Scan sempre têm `EntryRow.SourceFile == ""`, então o handler tentava
remover a chave só de `ingame_edits.json` (via `ReadFile`/`dict.Remove`/
`WriteFile`).

**Impacto**: alto (perda de confiança na ação, não de dados). Se a
tradução da linha já existia num arquivo curado (ex.: `ui/hud.json`) em
vez de `ingame_edits.json`, `dict.Remove(row.Original)` retornava `false`
e `WriteFile` nunca era chamado — mas o código continuava e removia a
linha da LISTA e do `CaptureRegistry` incondicionalmente. Resultado: o
usuário via a linha sumir (parecia ter funcionado), mas a entrada
continuava intacta no arquivo real. Reportado pelo usuário como "o botão
Del não está funcionando corretamente" — sintoma confirmado: "some de
verdade [da lista], mas o arquivo JSON continua com a entrada".

**Status**: corrigido. Quando a remoção em `ingame_edits.json` não
encontra a chave, o handler agora consulta `TranslationIndex` (via novo
`Overlay/CorpusFileResolver.cs`) para achar o arquivo real onde a chave
está definida, e remove de lá (mesmo caminho que a Biblioteca já usava
corretamente para suas próprias linhas). Só se a chave não existir em
lugar nenhum do corpus é que nada é removido de fato — mas isso já não
deveria acontecer, dado que a linha só existe na lista porque foi
capturada/carregada de algum lugar.

> **Segunda camada do mesmo bug, achada ao testar a correção acima**: o
> arquivo em disco passou a ser corrigido corretamente, mas o usuário
> reportou que a tradução "fantasma" continuava aparecendo em verde na
> GUI até reiniciar o jogo. Causa raiz: `TranslationRepository.ReloadFile`
> (`Repository/TranslationRepository.cs`) foi desenhado só para
> ADICIONAR/ATUALIZAR chaves presentes no arquivo recarregado — nunca
> detectava uma chave que tinha sumido do arquivo (removida), então ela
> ficava presa em `_translations` (dicionário em memória) para sempre,
> até o próximo `LoadAll` do zero (reinício do jogo). Corrigido:
> `ReloadFile` agora compara as chaves que o arquivo declarava ANTES
> desta recarga (`_keysByFileMap`) com as que declara agora, e remove de
> `_translations`/`_sourceFileMap` qualquer chave que sumiu — mas SÓ se
> este arquivo ainda for quem "vence" para ela em memória (se outra
> camada/override já a sobrescreveu, não mexe, para não apagar uma
> tradução que continua válida). Novo `TranslationKey.Parse(string)`
> (inverso de `ToString()`) permite reconstruir a chave estruturada a
> partir da forma string armazenada em `_keysByFileMap`/`_sourceFileMap`.
> Isto não é exclusivo do botão Del — qualquer remoção de chave de um
> arquivo (edição manual externa incluída, via hot reload) tinha esse
> mesmo problema; a correção resolve a causa raiz no repositório, não só
> o sintoma no botão.

**Dificuldade**: baixa (reaproveita `TranslationIndex`/`TranslationFileStore`
já existentes; a correção no repositório reaproveita `_keysByFileMap`,
que já existia para outro propósito).

**Dependências**: nenhuma.

---

## Resumo por prioridade prática

| # | Item | Impacto | Dificuldade | Bloqueia | Status |
|---|---|---|---|---|---|
| 15 | Sem git | Alto (indireto) | Trivial | Tudo — fazer primeiro | **Adiado por decisão do usuário** — usando backup manual em pasta timestampada (`_backup_YYYYMMDD_HHMMSS/`) até o projeto estar "100%" |
| 8 | Escrita não-atômica de `ingame_edits.json` | Médio-alto (perda de dados) | Trivial | — | **Corrigido 2026-09-21** |
| 1 | UI.Text não interceptado | Alto | Baixa-média | Item 5 | **Corrigido 2026-09-21** |
| 3 | Merge last-write-wins silencioso | Alto | Baixa | Fase 2 (storage) | **Corrigido 2026-09-21** |
| 2 | Identidade só-texto sem contexto | Alto (latente) | Média | Fase 2 (storage) | **Corrigido 2026-09-21** — inferência automática via GetCategory, aditiva/sem risco (fallback sempre existe) |
| 16 | Entradas ignoradas descartadas ao carregar (bug novo, achado na Fase 3) | Alto (ruído em _capture, não visível ao jogador) | Baixa | — | **Corrigido 2026-09-21** |
| 17 | `ReadFile`/`WriteFile` corrompiam forma de objeto (bug novo, achado na Fase 4) | Alto (risco de corrupção, pego antes de afetar dados reais) | Média | — | **Corrigido 2026-09-21** |
| 9 | Bloqueio de mouse não cobre modal | Médio | Trivial | — | **Corrigido 2026-09-21** |
| 10 | "Ignorar" como identidade | Médio | Baixa | Fase 2 (storage) | **Corrigido por completo 2026-09-21** — Scan e Biblioteca agora gravam `ignore:true` explícito |
| 11 | Biblioteca incompleta | Médio | Média | Fase 2 (storage) | **Corrigido 2026-09-21** — Biblioteca lê/escreve o corpus inteiro via `TranslationIndex`/`TranslationFileStore` |
| 5 | Polling permanente | Médio | Média | Item 1 | **Mantido por decisão do usuário** — não será removido |
| 4 | Normalização duplicada | Médio | Média | Suíte de testes manuais | **Corrigido parcialmente 2026-09-21** — strip de rich text unificado; normalização de placeholders permanece só em TMPTextPatch por decisão consciente (não duplicada, risco alto para mover) |
| 7 | Reload O(total) | Baixo (hoje) | Baixa | — | **Corrigido 2026-09-21** — ConcurrentDictionary, O(arquivo alterado) |
| 6 | `_scan_debug.txt` sempre escrito | Baixo | Trivial | — | **Corrigido 2026-09-21** |
| 13 | Índice morto | Baixo | Trivial | Fase 2 | **Corrigido 2026-09-21** — substituído por `TranslationIndex` |
| 14 | `npc_names.json` sem hot reload | Baixo | Trivial | — | **Corrigido 2026-09-21** — achado bônus: também estava vazando pro dicionário de traduções, corrigido junto |
| 12 | GUI monolítica | Médio (manutenção) | Alta | Fase 3, depois do storage | **Concluído 2026-09-22** — suíte de testes + 8 fatias de lógica extraída, e arquivo dividido em 12 partial class files por assunto. Separação ViewModel/Renderer "de verdade" (com `EntryRow` desacoplado) permanece não feita, por decisão consciente (ver nota completa no item 12) |
| 18 | Biblioteca engasgava ao abrir (corpus grande) | Alto (percebido em teste real) | Baixa-média | — | **Corrigido 2026-09-21** |
| — | Mover/copiar entre arquivos na Biblioteca | Médio | Média | — | **Corrigido 2026-09-21** — painel "Mover/Copiar" reaproveitando o checkbox "Sel." |
| 19 | Botão "Del" do Scan não removia entradas de arquivos curados | Alto (confiança na ação) | Baixa | — | **Achado pelo usuário e corrigido 2026-09-22** — fallback via `TranslationIndex`/`CorpusFileResolver` |
