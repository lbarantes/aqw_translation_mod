# AUTO_TRANSLATION.md — Tradução automática

> **Status de implementação (2026-09-21, revisado no mesmo dia)**:
> implementado. Ver `Translation/ITranslationProvider.cs`,
> `ManualTranslationProvider.cs`, `MyMemoryTranslationProvider.cs`,
> `AutoTranslationOrchestrator.cs`, e a integração na GUI (checkbox "Sel."
> por linha + botões "Marcar visíveis"/"Desmarcar" + "Auto Traduzir" +
> painel de preview em `Overlay/TranslationEditorOverlay.cs`).
>
> **Revisão de UX (mesmo dia)**: a primeira versão operava sobre "tudo que
> estiver visível no momento do clique" (busca/categoria como seleção
> implícita). O usuário pediu explicitamente para poder **escolher
> linha a linha ANTES de clicar** em Auto Traduzir, em vez de o botão
> decidir sozinho a partir do filtro atual. Voltou a ter checkbox por
> linha na lista principal (`EntryRow.Selected`, persistido em
> `_selectedKeys` entre reconstruções da lista), com "Marcar visíveis"/
> "Desmarcar" como atalhos para não exigir clicar caixa por caixa. O botão
> "Auto Traduzir" agora só opera sobre `_rows.Where(r => r.Selected)` —
> nunca mais escolhe implicitamente a partir do filtro.
>
> `ITranslationProvider` continua sem `TranslateBatchAsync`/`MaxBatchSize`
> (YAGNI — nenhum provedor real precisa ainda). Não testado contra a API
> real em jogo (ambiente sem rede/jogo) — ver `docs/ROADMAP.md` Fase 5
> para o que falta validar.

## 1. Reaproveitamento do que já existe

Duas peças do sistema atual já resolvem metade do problema difícil deste
recurso, e devem ser **reusadas, não reimplementadas**:

- **Proteção de tags Unity**: `RichTextStage`/`RichTextUtils` já fazem
  strip→placeholder→reinject de tags TMP de forma robusta (regex com lista
  explícita de tags suportadas, suporta aninhamento via índices numerados).
  Para mandar texto a uma API externa: usar exatamente esse
  strip-antes-de-enviar / reinject-depois-de-receber.
- **Proteção de placeholders de jogo**: a normalização `{player}/{n}/{map}/
  {npc}` já existente (hoje em `TMPTextPatch`, proposta para migrar a
  `Pipeline/Stages` em `ARCHITECTURE.md` §5) já produz um texto com
  placeholders `{token}` canônicos. É esse texto — rich-text-stripped e
  placeholder-normalizado — que deve ser enviado ao provedor de tradução,
  nunca o texto bruto do jogo.

## 2. Abstração de provedor (`ITranslationProvider`)

```csharp
public interface ITranslationProvider
{
    string Id { get; }              // "manual" | "libretranslate" | "google-cloud" | ...
    bool RequiresApiKey { get; }
    int MaxBatchSize { get; }       // 1 se o provedor não suporta lote

    Task<TranslationProviderResult> TranslateAsync(
        TranslationProviderRequest request, CancellationToken ct);

    Task<IReadOnlyList<TranslationProviderResult>> TranslateBatchAsync(
        IReadOnlyList<TranslationProviderRequest> requests, CancellationToken ct);
}

public sealed class TranslationProviderRequest
{
    public string SourceText { get; init; }   // já protegido (sem rich text, com {tokens})
    public string SourceLang { get; init; }
    public string TargetLang { get; init; }
}

public sealed class TranslationProviderResult
{
    public bool Success { get; init; }
    public string? TranslatedText { get; init; }
    public string? ErrorMessage { get; init; }
    public string ProviderId { get; init; }
    public long LatencyMs { get; init; }
}
```

Implementações previstas (ainda não implementadas):
- `ManualTranslationProvider` — no-op, representa "sem auto-tradução"
  (é o default de `ModConfig`, garantindo que o recurso é opt-in).
- Um provedor de API pública real, plugável via config (nome/URL/chave),
  sem acoplar o restante do sistema a um fornecedor específico.
- Espaço reservado para futuros provedores pagos/self-host, sem mudança de
  contrato.

## 3. Orquestração — política fica fora do provedor

Um `AutoTranslationOrchestrator` (novo) concentra tudo que **não** é
específico de um provedor, para não duplicar em cada implementação:

- **Rate limiting** (token bucket simples) e **retry com backoff**.
- **Timeout** por requisição e **cancelamento** (um `CancellationTokenSource`
  por lote — é o que o botão "Cancelar" do preview, `GUI_ARCHITECTURE.md`
  §5, precisa para funcionar).
- **Cache de tentativas de auto-tradução** — separado do `TranslationCache`
  de runtime (que é para texto já traduzido em produção); este cache evita
  reconsultar/re-cobrar a mesma string já auto-traduzida antes, mesmo que
  o resultado ainda não tenha sido aceito pelo usuário.
- **Execução assíncrona fora da main thread**, com o resultado devolvido à
  GUI/estado via `UnityMainThreadDispatcher` — reaproveitando o componente
  que já existe para o hot reload, em vez de criar um segundo mecanismo de
  marshalling.
- **Validação pós-tradução**: conta quantos `{tokens}` existiam no texto
  enviado e quantos sobreviveram no texto recebido; se o número não bater,
  a tradução automática é marcada como suspeita (não aplicada
  automaticamente, exige revisão manual explícita no preview) em vez de
  arriscar corromper um placeholder.
- **Regra de não-sobrescrita** (inegociável): antes
  de propor substituir uma entrada existente, o orquestrador olha
  `status`/`source` da entrada (`TRANSLATION_FORMAT.md` §4). Uma entrada
  com `source:"manual"` só é sobrescrita se o usuário marcar
  explicitamente a checkbox dela no preview e confirmar "Sobrescrever
  selecionadas" — nunca por um botão único de "traduzir tudo".

## 4. UX (resumo — detalhe completo em `GUI_ARCHITECTURE.md` §5)

Seleção → preview (original / manual atual / proposta automática, por
linha, com checkbox) → "Preencher só vazios" **ou** "Sobrescrever
selecionadas" **ou** "Cancelar". Nada é gravado antes da confirmação
explícita nessa tela.

## 5. O que o sistema deve recusar a fazer

- Não substitui uma tradução com `status:"reviewed"` mesmo se selecionada,
  sem uma confirmação adicional (dois cliques em vez de um, ou uma cor de
  aviso diferente no preview) — é a entrada com maior custo de perda se
  sobrescrita por engano.
- Não envia ao provedor nenhuma entrada marcada com a tag
  `"do-not-auto-translate"` (`TRANSLATION_FORMAT.md` §4).
- Não trava o jogo nem a GUI enquanto espera resposta de rede — todo o
  fluxo é assíncrono, com um indicador de progresso e cancelamento
  disponível a qualquer momento.
- Não assume que a internet está disponível — falha de rede em qualquer
  etapa apenas marca aquelas linhas como "falhou" no preview e continua as
  demais; não aborta o lote inteiro por causa de um erro de conexão pontual.
