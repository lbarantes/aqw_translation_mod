# ADR-0001 — Identidade das traduções: texto normalizado + contexto opcional

> **Status de implementação (2026-09-21)**: a infraestrutura desta decisão
> está implementada — `Core/TranslationKey.cs` (struct texto+contexto),
> `TranslationRepository.TryGet(key, context, out value)`,
> `LookupStage`/`TranslationContext`/`PipelineContext` propagando `Context`.
> **Não implementado ainda**: nenhum chamador (`TMPTextPatch`) preenche
> `Context` automaticamente — a inferência automática continua sendo
> trabalho futuro opcional, como já previsto na seção "Consequências"
> abaixo. Ver `docs/TECHNICAL_DEBT.md` #2 para o estado exato.

## Contexto

O sistema atual identifica uma tradução exclusivamente pelo texto original,
já normalizado (rich text removido, `{player}/{n}/{map}/{npc}` canonizados —
ver `CURRENT_SYSTEM.md` §4.1). Nenhuma informação de hierarquia Unity,
componente ou cena participa da chave de busca (`LookupStage` só recebe a
string). Uma alternativa seria combinar
texto com caminho de `GameObject`, cena, componente, hierarquia, hash, etc.

## Problema

O mesmo texto-fonte pode legitimamente precisar de traduções diferentes em
lugares diferentes (ex.: "Attack" como nome de skill vs. como botão de
ataque automático). O sistema atual não tem como representar isso — sempre
retorna a mesma tradução, sem aviso de colisão. Ao mesmo tempo, qualquer
identidade baseada em hierarquia/`GameObject` enfrenta um problema real e
verificado no código: `InstanceID` só vive durante o processo
(`ComponentPathCache` já trata isso como cache efêmero, nunca como algo
persistível); nomes de `GameObject` mudam com atualizações do jogo (o jogo
é citado como atualizado semanalmente); e a Unity não expõe nenhum
identificador estável de objeto de cena em runtime para um mod consumir.

## Decisão

A identidade primária de uma tradução continua sendo o **texto-fonte
normalizado** (mesma normalização já implementada — reaproveitada, não
substituída). Adiciona-se um campo **opcional** `context` (string livre,
curta, atribuída por um humano — ex.: `"skill"`, `"ui.button"`,
`"quest.title"`), formando uma chave composta `texto@@context` quando
presente. A resolução tenta `texto@@context` primeiro (quando o chamador
consegue sugerir um contexto), depois cai para `texto` puro (comportamento
atual, sempre disponível como fallback).

Isso segue o padrão consolidado de `msgctxt` do gettext e de qualificadores
de contexto de outros sistemas de i18n maduros — não é uma invenção nova,
é a solução conhecida para exatamente este problema.

## Alternativas consideradas

1. **Caminho completo de `GameObject` como chave.** Rejeitado: instável
   entre atualizações do jogo (exigiria remigrar toda vez que a UI do jogo
   mudasse), impraticável de manter em um corpus comunitário versionado, e
   sem identificador estável disponível em runtime para ancorar isso.
2. **Hash do conteúdo/contexto combinado.** Rejeitado: ofusca a chave sem
   resolver o problema de fundo — ainda seria preciso decidir *o que* entra
   no hash (mesma pergunta de fundo), e perde a legibilidade humana do
   texto como chave (importante para revisão de PR/diff).
3. **Contexto obrigatório em toda entrada.** Rejeitado: explodiria o corpus
   existente (centenas de entradas já vivas hoje sem nenhum contexto) sem
   necessidade — a esmagadora maioria dos textos não é ambígua.
4. **Não fazer nada (manter só texto, aceitar o risco de colisão).**
   Rejeitado: é um risco
   real, não hipotético, à medida que o corpus cresce para cobrir sagas
   inteiras com vocabulário reaproveitado.

## Consequências

- Compatibilidade total com o corpus existente (nenhuma entrada precisa de
  `context` para continuar funcionando).
- A GUI precisa de uma ação explícita ("desambiguar por contexto") para
  criar a segunda variante quando um tradutor perceber a colisão —
  detectar colisões automaticamente antes que aconteçam não é possível sem
  saber a intenção semântica do texto, então esta decisão aceita que a
  desambiguação é **reativa** (feita quando alguém percebe o problema), não
  preventiva.
- `TranslationRepository`/`CollectionLoader` precisam suportar
  `Dictionary<CompositeKey, string>` em vez de `Dictionary<string,string>`
  puro — mudança de tipo interna, sem impacto no formato de arquivo (a
  chave composta é só a combinação do valor `context` do JSON com a chave
  de texto do JSON, nunca serializada como uma única string concatenada em
  disco).
