using System.Collections.Generic;
using AQWMod.Localization.Overlay;
using AQWMod.Localization.Translation;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class AutoPreviewPlannerTests
    {
        private static AutoTranslationOrchestrator.TranslationOutcome Outcome(
            string key, bool success, string? text = null, bool mismatch = false, string? error = null) =>
            new AutoTranslationOrchestrator.TranslationOutcome
            {
                OriginalKey = key,
                Success = success,
                TranslatedText = text,
                PlaceholderMismatch = mismatch,
                Error = error,
            };

        [Fact]
        public void OutcomeNotInSelection_IsSkippedEntirely()
        {
            var saved = new Dictionary<string, string>(); // "Attack" nunca foi selecionado
            var result = AutoPreviewPlanner.Plan(saved, new[] { Outcome("Attack", true, "Ataque") });

            Assert.Empty(result.Items);
            Assert.Equal(0, result.Ok);
            Assert.Equal(0, result.Failed);
        }

        [Fact]
        public void FailedOutcome_CountsAsFailed_NoPreviewItem()
        {
            var saved = new Dictionary<string, string> { ["Attack"] = "" };
            var result = AutoPreviewPlanner.Plan(saved, new[] { Outcome("Attack", false, error: "timeout") });

            Assert.Empty(result.Items);
            Assert.Equal(1, result.Failed);
        }

        [Fact]
        public void SuccessNoMismatch_NoExistingTranslation_IsPreCheckedAndCountsOk()
        {
            var saved = new Dictionary<string, string> { ["Attack"] = "" }; // sem tradução existente
            var result = AutoPreviewPlanner.Plan(saved, new[] { Outcome("Attack", true, "Ataque") });

            var item = Assert.Single(result.Items);
            Assert.True(item.DefaultChecked);
            Assert.Equal(1, result.Ok);
            Assert.Equal(0, result.Suspicious);
        }

        [Fact]
        public void PlaceholderMismatch_IsNeverPreChecked_CountsAsSuspicious()
        {
            var saved = new Dictionary<string, string> { ["Attack"] = "" };
            var result = AutoPreviewPlanner.Plan(saved, new[] { Outcome("Attack", true, "Ataque", mismatch: true) });

            var item = Assert.Single(result.Items);
            Assert.False(item.DefaultChecked);
            Assert.Equal(1, result.Suspicious);
            Assert.Equal(0, result.Ok);
        }

        [Fact]
        public void ExistingDifferentTranslation_IsNotPreChecked_ToAvoidSilentOverwrite()
        {
            // "Attack" já tem uma tradução salva diferente do original —
            // não deve vir pré-marcado, mesmo com sucesso e sem mismatch.
            var saved = new Dictionary<string, string> { ["Attack"] = "Ataque (manual)" };
            var result = AutoPreviewPlanner.Plan(saved, new[] { Outcome("Attack", true, "Ataque (auto)") });

            var item = Assert.Single(result.Items);
            Assert.False(item.DefaultChecked);
            Assert.Equal(1, result.Ok); // ainda conta como "pronta", só não vem marcada
        }

        [Fact]
        public void ExistingIdentitySavedValue_IsTreatedAsNoExistingTranslation_PreChecked()
        {
            // saved == original é "ignorado" (Saved == Original), não uma
            // tradução real — não deve bloquear o pré-marcado.
            var saved = new Dictionary<string, string> { ["OK"] = "OK" };
            var result = AutoPreviewPlanner.Plan(saved, new[] { Outcome("OK", true, "Certo") });

            var item = Assert.Single(result.Items);
            Assert.True(item.DefaultChecked);
        }

        [Fact]
        public void MultipleOutcomes_AggregatesCountersCorrectly()
        {
            var saved = new Dictionary<string, string>
            {
                ["A"] = "",
                ["B"] = "",
                ["C"] = "",
            };
            var outcomes = new[]
            {
                Outcome("A", true, "Traduzido A"),
                Outcome("B", true, "Traduzido B", mismatch: true),
                Outcome("C", false, error: "erro"),
            };

            var result = AutoPreviewPlanner.Plan(saved, outcomes);

            Assert.Equal(1, result.Ok);
            Assert.Equal(1, result.Suspicious);
            Assert.Equal(1, result.Failed);
            Assert.Equal(2, result.Items.Count); // A e B viram item; C (falhou) não
        }
    }
}
