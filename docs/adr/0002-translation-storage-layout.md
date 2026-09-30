# ADR-0002 — Organização de arquivos: pastas como navegação, não como identidade

> **Status de implementação (2026-09-21)**: implementado em
> `TranslationRepository.LoadAll`/`MergeLayer` — camada `overrides/`
> (com alias legado `ingame_edits.json` na raiz) sempre vence; conflitos
> dentro da mesma camada são logados como `WARNING`, nunca resolvidos pela
> ordem do sistema de arquivos. `TranslationIndex` mantém a proveniência
> arquivo↔chave. Não foi criada uma classe `CollectionLoader` separada
> (ver nota em `docs/ROADMAP.md` Fase 3) — a lógica vive dentro do próprio
> `TranslationRepository`, por não haver ainda um segundo consumidor que
> justifique a divisão.

## Contexto

Hoje todas as traduções são carregadas de `translations/**/*.json`
recursivamente (`TranslationRepository.LoadAll`) e fundidas em **um único**
dicionário achatado, sem registrar de qual arquivo cada chave veio (exceto
um índice por nome-de-arquivo hoje sem consumidor real,
`TECHNICAL_DEBT.md` #13). `ingame_edits.json` é um arquivo plano na raiz,
carregado exatamente como qualquer outro. A meta é uma estrutura tipo
`Translations/Maps/Pirates/rhuibarb.json` navegável como uma "biblioteca".

## Problema

Duas necessidades em tensão aparente: (1) organizar fisicamente por
conteúdo (mapa, classe, quest) para curadoria humana, e (2) a chave de
tradução (`ADR-0001`) não deve depender de onde o arquivo mora, porque
mover uma entrada entre arquivos não pode exigir remapear
identidade.

## Decisão

Pastas e nomes de arquivo são **puramente organizacionais** — nunca
participam da chave de lookup em runtime. A estrutura proposta
(`translations/ui/`, `classes/`, `maps/<Saga>/`, `quests/`, `npcs/`,
`overrides/`, `_capture/`) é uma convenção de navegação para humanos e para
a GUI (via `TranslationIndex`, que mapeia chave→arquivo e arquivo→chaves),
não uma parte do esquema de identidade.

A prioridade de resolução em caso de colisão é definida por **camada**, não
por pasta individual: conteúdo curado (qualquer pasta fora de `overrides/`)
tem prioridade normal entre si; `overrides/ingame_edits.json` sempre vence
(ver `ARCHITECTURE.md` §3). Colisões dentro da mesma camada são reportadas
como aviso de log, nunca resolvidas silenciosamente por ordem de
enumeração do sistema de arquivos (o comportamento atual,
`TECHNICAL_DEBT.md` #3).

## Alternativas consideradas

1. **Pasta como parte da chave** (ex.: `maps/pirates/rhuibarb.json#Attack`).
   Rejeitado: acopla identidade a organização física, tornando "mover
   tradução entre arquivos" uma operação de migração de
   referências em vez de uma edição de arquivo trivial.
2. **Um arquivo monolítico só, sem subpastas.** Rejeitado: inviabiliza
   revisão de PR por escopo pequeno, dificulta carregamento
   seletivo futuro, e contraria o objetivo de organização por conteúdo.
3. **Banco de dados (SQLite) em vez de JSON.** Rejeitado por ora: JSON
   é revisável em diff/PR por humanos, editável à mão em emergência, e já
   é o formato que todo o pipeline existente entende — trocar o formato de
   armazenamento não resolve nenhum problema listado em
   `TECHNICAL_DEBT.md` e adicionaria uma dependência nova sem justificativa
   (violaria a regra de não adicionar dependência sem
   necessidade clara).

## Consequências

- `TranslationRepository` precisa manter, além do dicionário achatado
  atual (mantido para o hotpath de lookup, sem mudança de performance),
  um índice de proveniência (`TranslationIndex`, `ARCHITECTURE.md` §4)
  construído no mesmo passe de carregamento.
- Mover/copiar uma entrada entre arquivos na GUI é uma operação local
  (remove de um dict, insere em outro, reescreve os dois arquivos) — sem
  qualquer efeito sobre como o runtime resolve a tradução depois.
- `ingame_edits.json` passa a viver conceitualmente na camada
  `overrides/` (ver `MIGRATION.md`), mas continua sendo, na prática, um
  arquivo JSON igual a qualquer outro — só sua **camada de prioridade** é
  especial, não seu formato.
