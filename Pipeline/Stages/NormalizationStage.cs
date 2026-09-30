using AQWMod.Localization.Core;
using AQWMod.Localization.Utils;

namespace AQWMod.Localization.Pipeline.Stages
{
    // Normaliza whitespace antes do lookup: o jogo mistura \r\n e \n entre
    // sistemas, o backend às vezes concatena com espaço duplo, e trailing
    // whitespace impede o match com a chave do JSON.
    public sealed class NormalizationStage : IPipelineStage
    {
        public bool Process(PipelineContext ctx)
        {
            ctx.CurrentText = StringNormalizer.Normalize(ctx.CurrentText);
            return true;
        }
    }
}
