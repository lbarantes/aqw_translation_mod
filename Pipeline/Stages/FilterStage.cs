using AQWMod.Localization.Core;

namespace AQWMod.Localization.Pipeline.Stages
{
    // Descarta texto que definitivamente não precisa de tradução. Regra geral:
    // melhor deixar passar algo desnecessário (cache miss barato) do que
    // filtrar algo que devia ser traduzido — então só filtra casos
    // inequívocos (vazio, curto demais, só número/símbolo, URL). Palavra
    // solta, texto com acento ou texto curto tipo "HP"/"OK" passam normal;
    // detectar PT-BR fica só no MissingTranslationCollector.
    public sealed class FilterStage : IPipelineStage
    {
        public bool Process(PipelineContext ctx)
        {
            var text = ctx.OriginalText;

            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            {
                ctx.ShouldCapture = false;
                return false;
            }

            if (IsNumericOrSymbolOnly(text))
            {
                ctx.ShouldCapture = false;
                return false;
            }

            if (text.StartsWith("http://") || text.StartsWith("https://") || text.StartsWith("www."))
            {
                ctx.ShouldCapture = false;
                return false;
            }

            return true;
        }

        private static bool IsNumericOrSymbolOnly(string text)
        {
            foreach (char c in text)
                if (char.IsLetter(c)) return false;
            return true;
        }
    }
}
