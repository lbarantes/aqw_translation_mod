using System.Text.RegularExpressions;
using AQWMod.Localization.Core;

namespace AQWMod.Localization.Pipeline.Stages
{
    // Normaliza formatos de placeholder pro canônico {nome}: o backend usa
    // {0}, {quest_name}, %name%, [[value]] misturados, mas o repositório
    // sempre armazena com {nome}. Não resolve os valores aqui (não temos
    // acesso aos dados reais do jogo) — o texto traduzido mantém {quest_name}
    // como placeholder pro jogo resolver depois.
    public sealed class PlaceholderStage : IPipelineStage
    {
        private static readonly Regex PercentRegex  = new Regex(@"%(\w+)%",    RegexOptions.Compiled);
        private static readonly Regex BracketsRegex = new Regex(@"\[\[(\w+)\]\]", RegexOptions.Compiled);

        public bool Process(PipelineContext ctx)
        {
            var text = ctx.CurrentText;

            if (text.Contains('%'))
                text = PercentRegex.Replace(text, "{$1}");

            if (text.Contains('['))
                text = BracketsRegex.Replace(text, "{$1}");

            ctx.CurrentText = text;
            return true;
        }
    }
}
