using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Translation;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class AutoTranslationOrchestratorTests
    {
        // BuildCandidate

        [Fact]
        public void BuildCandidate_PlainText_NoTagsNoNumbers()
        {
            var c = AutoTranslationOrchestrator.BuildCandidate("Attack", "Attack");
            Assert.Equal("Attack", c.TextToSend);
            Assert.Empty(c.Tags);
            Assert.False(c.RestoreNumbersOnOutput);
            Assert.Equal(0, c.PlaceholderCount);
        }

        [Fact]
        public void BuildCandidate_WithRichText_StripsTagsForSending()
        {
            var c = AutoTranslationOrchestrator.BuildCandidate("Attack", "<color=red>Attack</color>");
            Assert.DoesNotContain("<color", c.TextToSend);
            Assert.Equal(2, c.Tags.Count);
        }

        [Fact]
        public void BuildCandidate_WithLooseNumbers_NormalizesAndFlagsRestore()
        {
            var c = AutoTranslationOrchestrator.BuildCandidate("key", "1/30 Frogzards Defeated");
            Assert.Equal("{n1}/{n2} Frogzards Defeated", c.TextToSend);
            Assert.True(c.RestoreNumbersOnOutput);
            Assert.Equal(new[] { "1", "30" }, c.CapturedNumbers);
            Assert.Equal(2, c.PlaceholderCount);
        }

        [Fact]
        public void BuildCandidate_AlreadyTokenized_DoesNotRenumberTokenNames()
        {
            // "{n1}/{n2} Frogzards Defeated" já é uma chave normalizada — não
            // pode reprocessar o "1"/"2" que faz parte dos NOMES dos tokens.
            var c = AutoTranslationOrchestrator.BuildCandidate("key", "{n1}/{n2} Frogzards Defeated");
            Assert.Equal("{n1}/{n2} Frogzards Defeated", c.TextToSend);
            Assert.False(c.RestoreNumbersOnOutput);
            Assert.Empty(c.CapturedNumbers);
        }

        // TranslateBatchAsync — round trip completo via provedor "eco"

        [Fact]
        public async Task TranslateBatchAsync_RichTextAndNumbers_RoundTripsThroughEchoProvider()
        {
            var provider = FakeTranslationProvider.Echo();
            var orchestrator = new AutoTranslationOrchestrator(provider);

            var original = "<color=red>Attack</color> 1/30 done";
            var candidate = AutoTranslationOrchestrator.BuildCandidate("key", original);

            var results = await orchestrator.TranslateBatchAsync(
                new[] { candidate }, "en", "pt-BR", CancellationToken.None);

            var outcome = Assert.Single(results);
            Assert.True(outcome.Success);
            Assert.False(outcome.PlaceholderMismatch);
            Assert.Equal(original, outcome.TranslatedText); // eco -> restaura tudo de volta ao original
        }

        [Fact]
        public async Task TranslateBatchAsync_PlaceholderCountChanges_FlagsMismatch()
        {
            // Provedor "corrompe" a tradução removendo um placeholder — deve
            // ser sinalizado, não aplicado silenciosamente.
            var provider = new FakeTranslationProvider(req => new TranslationProviderResult
            {
                Success = true,
                TranslatedText = req.SourceText.Replace("{n2}", ""),
            });
            var orchestrator = new AutoTranslationOrchestrator(provider);
            var candidate = AutoTranslationOrchestrator.BuildCandidate("key", "1/30 done");

            var results = await orchestrator.TranslateBatchAsync(
                new[] { candidate }, "en", "pt-BR", CancellationToken.None);

            var outcome = Assert.Single(results);
            Assert.True(outcome.Success); // a chamada em si funcionou...
            Assert.True(outcome.PlaceholderMismatch); // ...mas o resultado é suspeito
        }

        [Fact]
        public async Task TranslateBatchAsync_SameTextTwice_OnlyCallsProviderOnce()
        {
            var provider = FakeTranslationProvider.Echo();
            var orchestrator = new AutoTranslationOrchestrator(provider);

            var c1 = AutoTranslationOrchestrator.BuildCandidate("key1", "Attack");
            var c2 = AutoTranslationOrchestrator.BuildCandidate("key2", "Attack"); // mesmo TextToSend

            var results = await orchestrator.TranslateBatchAsync(
                new[] { c1, c2 }, "en", "pt-BR", CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.Equal(1, provider.CallCount); // cache de sessão evitou a segunda chamada
        }

        [Fact]
        public async Task TranslateBatchAsync_ProviderAlwaysFails_ReturnsUnsuccessfulOutcomeAfterOneRetry()
        {
            var provider = FakeTranslationProvider.AlwaysFails("timeout simulado");
            var orchestrator = new AutoTranslationOrchestrator(provider);
            var candidate = AutoTranslationOrchestrator.BuildCandidate("key", "Attack");

            var results = await orchestrator.TranslateBatchAsync(
                new[] { candidate }, "en", "pt-BR", CancellationToken.None);

            var outcome = Assert.Single(results);
            Assert.False(outcome.Success);
            Assert.Equal("timeout simulado", outcome.Error);
            Assert.Equal(2, provider.CallCount); // 1 tentativa + 1 retry, depois desiste
        }

        [Fact]
        public async Task TranslateBatchAsync_ReportsProgressForEachCandidate()
        {
            var provider = FakeTranslationProvider.Echo();
            var orchestrator = new AutoTranslationOrchestrator(provider);
            var candidates = new List<AutoTranslationOrchestrator.Candidate>
            {
                AutoTranslationOrchestrator.BuildCandidate("k1", "One"),
                AutoTranslationOrchestrator.BuildCandidate("k2", "Two"),
            };

            var progressCalls = new List<(int done, int total)>();
            await orchestrator.TranslateBatchAsync(
                candidates, "en", "pt-BR", CancellationToken.None,
                onProgress: (done, total) => progressCalls.Add((done, total)));

            Assert.Equal(new (int, int)[] { (1, 2), (2, 2) }, progressCalls);
        }
    }
}
