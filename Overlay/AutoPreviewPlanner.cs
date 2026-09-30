using System.Collections.Generic;
using AQWMod.Localization.Translation;

namespace AQWMod.Localization.Overlay
{
    // Decide, pra uma leva de resultados de Auto Tradução, quais vêm
    // pré-marcadas no preview, e agrega os contadores do rodapé (pronta/
    // suspeita/falhou). Regra: sem sucesso conta como falha e não vira linha
    // de preview; PlaceholderMismatch conta como suspeita e nunca vem
    // pré-marcada (precisa revisão manual); já existe tradução diferente do
    // original salva pra essa chave também não vem pré-marcada (evita
    // sobrescrever sem o usuário perceber); senão, pronta e pré-marcada.
    // Puro dado/decisão — nenhum GameObject é criado aqui.
    internal static class AutoPreviewPlanner
    {
        public readonly struct PlannedItem
        {
            public AutoTranslationOrchestrator.TranslationOutcome Outcome { get; }
            public bool DefaultChecked { get; }

            public PlannedItem(AutoTranslationOrchestrator.TranslationOutcome outcome, bool defaultChecked)
            {
                Outcome = outcome;
                DefaultChecked = defaultChecked;
            }
        }

        public readonly struct Result
        {
            public List<PlannedItem> Items { get; }
            public int Ok { get; }
            public int Suspicious { get; }
            public int Failed { get; }

            public Result(List<PlannedItem> items, int ok, int suspicious, int failed)
            {
                Items = items;
                Ok = ok;
                Suspicious = suspicious;
                Failed = failed;
            }
        }

        /// <param name="savedByOriginal">Mapa Original → texto já salvo, só das linhas selecionadas para Auto Traduzir.</param>
        public static Result Plan(
            IReadOnlyDictionary<string, string> savedByOriginal,
            IEnumerable<AutoTranslationOrchestrator.TranslationOutcome> outcomes)
        {
            var items = new List<PlannedItem>();
            int ok = 0, suspicious = 0, failed = 0;

            foreach (var outcome in outcomes)
            {
                if (!savedByOriginal.TryGetValue(outcome.OriginalKey, out var saved)) continue;

                if (!outcome.Success)
                {
                    failed++;
                    continue; // sem linha de preview para falhas — nada a decidir
                }

                if (outcome.PlaceholderMismatch) suspicious++; else ok++;

                bool hasExisting = !string.IsNullOrEmpty(saved) && saved != outcome.OriginalKey;
                bool defaultChecked = !hasExisting && !outcome.PlaceholderMismatch;

                items.Add(new PlannedItem(outcome, defaultChecked));
            }

            return new Result(items, ok, suspicious, failed);
        }
    }
}
