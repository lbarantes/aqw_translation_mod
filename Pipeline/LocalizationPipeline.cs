using System.Collections.Generic;
using AQWMod.Localization.Core;
using AQWMod.Localization.Debug;
using AQWMod.Localization.Pipeline.Stages;

namespace AQWMod.Localization.Pipeline
{
    // Chain of Responsibility pra transformação de texto. Cada stage recebe um
    // PipelineContext mutável, pode mexer em CurrentText, marcar IsTranslated
    // = true, ou retornar false pra interromper o pipeline.
    //
    // Ordem dos stages importa:
    //   1. FilterStage        — descarta texto que não precisa de tradução
    //   2. NormalizationStage — normaliza whitespace antes do lookup
    //   3. RichTextStage (strip)  — extrai tags TMP, protege com placeholders
    //   4. PlaceholderStage   — normaliza {0}, %name% pra lookup consistente
    //   5. LookupStage        — busca no repositório
    //   6. RichTextStage (inject) — reinjeta tags no texto traduzido
    //   7. FallbackStage      — garante que volta o original se nada traduziu
    public sealed class LocalizationPipeline : ILocalizationPipeline
    {
        private readonly List<IPipelineStage> _stages;

        public LocalizationPipeline(ITranslationRepository repository, TranslationLogger logger)
        {
            _stages = new List<IPipelineStage>
            {
                new FilterStage(),
                new NormalizationStage(),
                new RichTextStage(strip: true),
                new PlaceholderStage(),
                new LookupStage(repository),
                new RichTextStage(strip: false),   // reinjecta após tradução
                new FallbackStage(),
            };
        }

        public PipelineResult Process(TranslationContext ctx)
        {
            var context = new PipelineContext(ctx);

            foreach (var stage in _stages)
            {
                if (!stage.Process(context))
                    break;
            }

            return new PipelineResult(context.CurrentText, context.IsTranslated);
        }
    }
}
