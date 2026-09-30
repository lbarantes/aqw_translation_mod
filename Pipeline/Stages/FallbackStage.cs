using AQWMod.Localization.Core;

namespace AQWMod.Localization.Pipeline.Stages
{
    // Se chegou até aqui o LookupStage não achou tradução — volta o
    // OriginalText intacto, senão o jogo mostraria um texto com placeholders
    // de rich text parcialmente removidos em vez do inglês original.
    public sealed class FallbackStage : IPipelineStage
    {
        public bool Process(PipelineContext ctx)
        {
            if (!ctx.IsTranslated)
                ctx.CurrentText = ctx.OriginalText;

            return false; // sempre finaliza o pipeline
        }
    }
}
