using AQWMod.Localization.Core;
using AQWMod.Localization.Utils;

namespace AQWMod.Localization.Pipeline.Stages
{
    // Busca a tradução no repositório, em cascata pra maximizar hit rate:
    // 1) texto atual (já normalizado pelos stages anteriores), 2) texto
    // original bruto, 3) normalização agressiva. No primeiro hit já marca
    // IsTranslated = true e para o pipeline.
    public sealed class LookupStage : IPipelineStage
    {
        private readonly ITranslationRepository _repo;

        public LookupStage(ITranslationRepository repo) => _repo = repo;

        public bool Process(PipelineContext ctx)
        {
            // ctx.Context, quando não-nulo, faz o repositório tentar
            // "texto@@contexto" antes de "texto" puro. Hoje é quase sempre
            // null (nenhum chamador ainda preenche), então não muda nada na
            // prática — só evita ter que voltar aqui quando alguém preencher.

            if (_repo.TryGet(ctx.CurrentText, ctx.Context, out var translated))
            {
                Apply(ctx, translated);
                return false; // pula direto pro RichTextStage(inject)
            }

            if (ctx.CurrentText != ctx.OriginalText &&
                _repo.TryGet(ctx.OriginalText, ctx.Context, out translated))
            {
                Apply(ctx, translated);
                return false;
            }

            var aggressive = StringNormalizer.NormalizeAggressive(ctx.CurrentText);
            if (aggressive != ctx.CurrentText && _repo.TryGet(aggressive, ctx.Context, out translated))
            {
                Apply(ctx, translated);
                return false;
            }

            return true; // sem hit, segue pro FallbackStage
        }

        private static void Apply(PipelineContext ctx, string translated)
        {
            ctx.CurrentText   = translated;
            ctx.IsTranslated  = true;
            ctx.ShouldCapture = false;
        }
    }
}
