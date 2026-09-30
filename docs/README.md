# Documentação — AQW Infinity Translation Mod

Índice de leitura recomendada.

1. [`CURRENT_SYSTEM.md`](CURRENT_SYSTEM.md) — como o sistema funciona hoje,
   lido diretamente do código (entrypoint, pipeline, interceptação,
   persistência, captura, GUI, config). Comece por aqui.
2. [`TECHNICAL_DEBT.md`](TECHNICAL_DEBT.md) — problemas concretos
   encontrados, com impacto/dificuldade/dependências e prioridade prática.
3. [`ARCHITECTURE.md`](ARCHITECTURE.md) — arquitetura futura proposta,
   ponto a ponto respondendo aos itens de `TECHNICAL_DEBT.md`.
4. [`TRANSLATION_FORMAT.md`](TRANSLATION_FORMAT.md) — schema dos arquivos
   de tradução (atual + extensões propostas, campo a campo justificado).
5. [`GUI_ARCHITECTURE.md`](GUI_ARCHITECTURE.md) — GUI atual mapeada
   funcionalidade a funcionalidade, e a separação de camadas proposta.
6. [`AUTO_TRANSLATION.md`](AUTO_TRANSLATION.md) — abstração de provedor,
   proteção de tags/placeholders, UX de confirmação.
7. [`REMOTE_UPDATES.md`](REMOTE_UPDATES.md) — manifesto e atualização
   segura via GitHub (ainda não implementado).
8. [`MIGRATION.md`](MIGRATION.md) — plano concreto de migração de
   `ingame_edits.json` para a nova organização.
9. [`ROADMAP.md`](ROADMAP.md) — fases incrementais, cada uma com objetivo,
   dependências, riscos, critério de conclusão e o que não fazer ainda.
10. [`adr/`](adr/) — decisões arquiteturais importantes, com alternativas
    consideradas:
    - [0001 — Identidade das traduções](adr/0001-translation-identity.md)
    - [0002 — Organização de arquivos](adr/0002-translation-storage-layout.md)
    - [0003 — Abstração de provedor de tradução automática](adr/0003-translation-provider-abstraction.md)
    - [0004 — Remover varredura em polling permanente](adr/0004-remove-polling-auto-scan.md)
