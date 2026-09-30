# ADR-0004 — Interceptar `UI.Text`; manter a varredura periódica por decisão de produto

> **Revisão (2026-09-21)**: a decisão original deste ADR (título anterior:
> "Substituir a varredura em polling permanente por interceptação + eventos
> de cena") foi parcialmente revertida a pedido explícito do usuário: *"o
> polling permanente (que é o que vai atualizando a lista de traduções)
> pode seguir... não está causando lag nem nada do tipo"*. A parte
> **implementada** é a interceptação de `UI.Text` (§Decisão, item 1) — a
> parte **descartada** é a substituição do timer de 1Hz por eventos de cena
> e a remoção do toggle "⟳ Auto" (§Decisão, itens 2-3, mantidos aqui
> riscados por transparência de histórico, não porque ainda valham).

## Contexto

Hoje existem três mecanismos de varredura de tela distintos, detalhados em
`CURRENT_SYSTEM.md` §7: (1) push barato no hotpath de tradução (grátis,
mantido), (2) uma varredura completa da cena a 1Hz, **sempre ativa**
independente do overlay estar aberto, cujo propósito duplo é descobrir
texto novo e reescrever `UnityEngine.UI.Text` (não interceptado por nenhum
patch Harmony), e (3) o toggle "⟳ Auto" da GUI, que adiciona uma segunda
varredura completa por segundo quando ligado. A proposta é
extinguir ou substituir esse mecanismo, preferindo eventos reais a
polling, mas também pede para não forçar uma arquitetura de eventos que a
Unity/BepInEx não suportem de verdade.

## Investigação

A Unity **não** expõe um evento genérico "qualquer texto de UI mudou". Os
únicos pontos de interceptação confiáveis para texto são os já explorados
pelo mod: o setter de propriedade, os métodos `SetText`, e `OnEnable` (para
texto serializado). Isso já cobre `TMP_Text` integralmente. `UnityEngine.
UI.Text.text`, ao contrário de `TMP_Text.text`, **é um setter não-abstrato**
— não há nenhum obstáculo técnico documentado (como o problema de bind em
método abstrato que forçou o uso de Postfix em `SetText`) para aplicar o
mesmo padrão de Prefix já usado com sucesso em `TMP_Text.text`. Já
`UnityEngine.SceneManagement.SceneManager.activeSceneChanged` é um evento
real e documentado da engine, sem necessidade de patch.

## Decisão

1. **[Implementado 2026-09-21]** Adicionar um patch Harmony em
   `UnityEngine.UI.Text.text` (Prefix) — `Interceptors/LegacyTextPatch.cs`.
   A tradução de `UI.Text` passa a ser event-driven no momento da escrita,
   como o TMP já é. O "enforcement-on-scan" deixa de ser necessário para
   este propósito, mas foi mantido (ver item 2) como rede de segurança.
2. ~~Substituir o timer perpétuo de descoberta (1Hz, sempre ativo) por um
   gatilho em `SceneManager.activeSceneChanged`.~~ **Descartado a pedido do
   usuário** (2026-09-21): a varredura de 1Hz é mantida — é o que atualiza
   a lista do overlay sem exigir clique manual em "Refresh", e o custo não
   é perceptível na prática de uso real. Com o item 1 implementado, essa
   varredura não é mais necessária para a *correção* da tradução — só para
   a *descoberta* de texto novo na lista, papel que continua cumprindo.
3. ~~Remover o toggle "⟳ Auto".~~ **Também mantido** — é opt-in, desligado
   por padrão, e o usuário não pediu para removê-lo.

## Alternativas consideradas

1. **Eliminar toda varredura, confiar 100% em patches.** Rejeitado por
   ora: mesmo depois do patch de `UI.Text`, a *descoberta* de texto (para
   preencher a lista da GUI ao abrir/trocar de mapa) ainda se beneficia de
   uma varredura pontual — o push do hotpath só registra o que já foi
   *escrito* via um caminho patcheado; texto que já estava na tela antes do
   overlay abrir pela primeira vez em uma sessão precisa de pelo menos uma
   varredura para aparecer na lista.
2. **Manter o timer de 1Hz, só reduzir a frequência.** Rejeitado: reduz o
   custo, mas mantém a categoria de problema (polling perpétuo sem relação
   com o que realmente mudou); o evento de troca de cena é um gatilho real
   disponível, não há razão para não usá-lo.
3. **Inventar um sistema de eventos próprio via reflection/proxy sobre todo
   componente de UI.** Rejeitado: complexidade alta, frágil a mudanças de
   versão do Unity/jogo, e resolve um problema (saber quando "qualquer
   coisa" mudou) que os patches pontuais já resolvem para os casos que
   importam.

## Consequências

- A *correção* da tradução de `UI.Text` deixou de depender da varredura —
  é instantânea, no momento da escrita, igual ao TMP.
- A varredura de 1Hz e o toggle "⟳ Auto" continuam consumindo CPU
  continuamente, por decisão consciente: o custo foi avaliado pelo usuário
  como imperceptível frente ao ganho de UX (lista sempre atualizada sem
  clique manual). Se isso mudar no futuro (corpus/cena muito mais pesados,
  relato de lag), a proposta original de troca por
  `SceneManager.activeSceneChanged` + refresh manual continua documentada
  acima e pode ser retomada sem redesenho — só depende de decisão de
  produto, não de investigação técnica adicional.
- `TMPTextPatch.LegacyOriginalTracker` agora é escrito tanto pelo novo
  patch quanto pelo "enforcement-on-scan" existente — os dois já
  respeitavam o mesmo contrato de campos (`Value`/`Category`/`Translated`),
  então não há conflito: o segundo vira, na prática, um no-op quando o
  patch já tiver traduzido o texto (mesmo valor, mesma comparação
  `tr != leg.text`).
