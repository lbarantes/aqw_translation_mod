# ADR-0003 — Abstração de provedor de tradução automática

> **Status de implementação (2026-09-21)**: implementado —
> `Translation/ITranslationProvider.cs`, `ManualTranslationProvider`,
> `MyMemoryTranslationProvider` (provedor público real, sem chave),
> `AutoTranslationOrchestrator` (rate limit/retry/cache/cancelamento/
> validação de placeholders), integrado na GUI via botão "Auto Traduzir" +
> painel de preview. A interface saiu mais simples do que a proposta
> original: sem `TranslateBatchAsync`/`MaxBatchSize`, porque nenhum
> provedor real implementado suporta lote de verdade ainda — ver
> `docs/AUTO_TRANSLATION.md` e `docs/ROADMAP.md` Fase 5 para detalhes.

## Contexto

O projeto prevê um botão "Auto Tradução" usando inicialmente uma API
pública, sem acoplar o sistema a um fornecedor
específico (a API pode mudar, exigir chave, sair do ar, ter rate limit).

## Problema

Sem uma abstração, a lógica de rede (timeout, retry, autenticação, formato
de request/response de um fornecedor específico) tende a se espalhar pela
GUI e pelo domínio, tornando caro trocar de fornecedor depois — exatamente
o risco que se quer evitar.

## Decisão

Introduzir `ITranslationProvider` (uma requisição/resposta por texto já
protegido de tags/placeholders, mais uma variante em lote) como único ponto
de contato com qualquer fornecedor externo, e um `AutoTranslationOrchestrator`
que concentra **tudo que não é específico de fornecedor**: rate limiting,
retry/backoff, timeout, cancelamento, cache de tentativas, validação de
placeholders pós-tradução, e a regra de nunca sobrescrever tradução manual
sem confirmação explícita. Ver `AUTO_TRANSLATION.md` para a interface
completa.

## Alternativas consideradas

1. **Chamar a API diretamente da GUI.** Rejeitado: acopla a interface de
   usuário a um fornecedor específico, exatamente o que se quer
   evitar; também dificulta testar a lógica de negócio sem rede real.
2. **Um único provedor "hardcoded" com flags de configuração.** Rejeitado:
   funciona até o dia em que o fornecedor mudar de API ou sair do ar — a
   interface plugável custa pouco a mais agora e evita reescrever tudo
   depois.
3. **Biblioteca de terceiros genérica de tradução multi-provedor.**
   Rejeitado por ora: adicionaria uma dependência externa não avaliada, sem
   necessidade comprovada — a interface própria é pequena o suficiente para
   não justificar a dependência (regra: não adicionar
   dependência sem justificar).

## Consequências

- Trocar de fornecedor no futuro é implementar uma nova classe
  `ITranslationProvider` e apontar `ModConfig` para ela — sem tocar em GUI,
  orquestrador, ou schema.
- Todo o trabalho de rede é assíncrono e roda fora da main thread, com
  resultado devolvido via `UnityMainThreadDispatcher` (componente já
  existente, reaproveitado) — nenhuma chamada HTTP bloqueante é introduzida
  no hotpath do jogo.
- A política de "nunca sobrescrever manual" depende do campo `source` do
  schema (`ADR` implícito em `TRANSLATION_FORMAT.md` §4) — este ADR está
  portanto acoplado à Fase 3 do roadmap (schema) antes de poder ser
  implementado de fato.
