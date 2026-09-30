using AQWMod.Localization.Interceptors;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // Cobre só as funções de normalização/restauração de placeholders — puro
    // manuseio de string, sem depender de nenhum objeto Unity vivo (Transform/
    // GameObject). GetCategory(Transform) e o patch em si (DoTranslate/
    // TranslateRaw, que dependem de componentes Unity reais) ficam fora do
    // escopo automatizável nesta configuração.
    public class TMPTextPatchNormalizationTests
    {
        // NormalizeNumbers / RestoreNumbers

        [Fact]
        public void NormalizeNumbers_ReplacesDigitRunsWithTokens()
        {
            var result = TMPTextPatch.NormalizeNumbers("0/3 Frogzards Defeated", out var captured);
            Assert.Equal("{n1}/{n2} Frogzards Defeated", result);
            Assert.Equal(new[] { "0", "3" }, captured);
        }

        [Fact]
        public void NormalizeNumbers_NoLetters_ReturnsUnchanged()
        {
            // Strings puramente numéricas (datas, IDs) não devem virar template —
            // não há "letra" nenhuma pra ancorar a tradução em torno do número.
            var result = TMPTextPatch.NormalizeNumbers("12345", out var captured);
            Assert.Equal("12345", result);
            Assert.Empty(captured);
        }

        [Fact]
        public void NormalizeNumbers_NoDigits_ReturnsUnchanged()
        {
            var result = TMPTextPatch.NormalizeNumbers("Frogzards Defeated", out var captured);
            Assert.Equal("Frogzards Defeated", result);
            Assert.Empty(captured);
        }

        [Fact]
        public void RestoreNumbers_PutsLiteralDigitsBack()
        {
            var restored = TMPTextPatch.RestoreNumbers("{n1}/{n2} derrotados", new[] { "0", "3" });
            Assert.Equal("0/3 derrotados", restored);
        }

        [Fact]
        public void RestoreNumbers_EmptyCaptured_ReturnsInputUnchanged()
        {
            Assert.Equal("sem tokens", TMPTextPatch.RestoreNumbers("sem tokens", System.Array.Empty<string>()));
        }

        [Fact]
        public void NormalizeThenRestore_RoundTrips()
        {
            var original = "Heal 50 HP";
            var normalized = TMPTextPatch.NormalizeNumbers(original, out var captured);
            var restored = TMPTextPatch.RestoreNumbers(normalized, captured);
            Assert.Equal(original, restored);
        }

        // NormalizeUsernamePlaceholders

        [Fact]
        public void NormalizeUsernamePlaceholders_ReplacesUnderscoreToken()
        {
            var result = TMPTextPatch.NormalizeUsernamePlaceholders("Awesome landing, Linck_!", out var captured);
            Assert.Equal("Awesome landing, {player}!", result);
            Assert.Equal(new[] { "Linck_" }, captured);
        }

        [Fact]
        public void NormalizeUsernamePlaceholders_NoUnderscoreOrNoSpace_ReturnsUnchanged()
        {
            Assert.Equal("NoSpaceHere_", TMPTextPatch.NormalizeUsernamePlaceholders("NoSpaceHere_", out var c1));
            Assert.Empty(c1);

            Assert.Equal("No underscore here", TMPTextPatch.NormalizeUsernamePlaceholders("No underscore here", out var c2));
            Assert.Empty(c2);
        }

        [Fact]
        public void NormalizeUsernamePlaceholders_MultipleUsers_NumbersSequentially()
        {
            var result = TMPTextPatch.NormalizeUsernamePlaceholders("Linck_ met Hero_AQW today", out var captured);
            Assert.Equal("{player} met {player2} today", result);
            Assert.Equal(new[] { "Linck_", "Hero_AQW" }, captured);
        }

        [Fact]
        public void NormalizeUsernamePlaceholders_PreservesSurroundingPunctuation()
        {
            var result = TMPTextPatch.NormalizeUsernamePlaceholders("Welcome, Linck_!", out var captured);
            Assert.Equal("Welcome, {player}!", result);
            Assert.Equal(new[] { "Linck_" }, captured);
        }

        // NormalizeNpcNames / RestoreNpcNames

        [Fact]
        public void NormalizeNpcNames_ReplacesKnownNpc()
        {
            TMPTextPatch.KnownNpcNames["ZzzBookerTestOne"] = "Bibliotecário";
            try
            {
                var result = TMPTextPatch.NormalizeNpcNames("Talk to ZzzBookerTestOne at intro.", out var captured);
                Assert.Equal("Talk to {npc} at intro.", result);
                Assert.Equal(new[] { "ZzzBookerTestOne" }, captured);
            }
            finally
            {
                TMPTextPatch.KnownNpcNames.Remove("ZzzBookerTestOne");
            }
        }

        [Fact]
        public void NormalizeNpcNames_UnknownWord_ReturnsUnchanged()
        {
            var result = TMPTextPatch.NormalizeNpcNames("Talk to NobodyKnown at intro.", out var captured);
            Assert.Equal("Talk to NobodyKnown at intro.", result);
            Assert.Empty(captured);
        }

        [Fact]
        public void RestoreNpcNames_UsesTranslatedNameWhenAvailable()
        {
            TMPTextPatch.KnownNpcNames["ZzzBookerTestTwo"] = "Bibliotecário";
            try
            {
                var restored = TMPTextPatch.RestoreNpcNames("Fale com {npc} na entrada.", new[] { "ZzzBookerTestTwo" });
                Assert.Equal("Fale com Bibliotecário na entrada.", restored);
            }
            finally
            {
                TMPTextPatch.KnownNpcNames.Remove("ZzzBookerTestTwo");
            }
        }

        [Fact]
        public void RestoreNpcNames_FallsBackToOriginalName_WhenNoTranslationStored()
        {
            // Entrada presente mas com valor vazio (== "manter sem tradução",
            // documentado no comentário do campo KnownNpcNames).
            TMPTextPatch.KnownNpcNames["ZzzBookerTestThree"] = "";
            try
            {
                var restored = TMPTextPatch.RestoreNpcNames("Fale com {npc} na entrada.", new[] { "ZzzBookerTestThree" });
                Assert.Equal("Fale com ZzzBookerTestThree na entrada.", restored);
            }
            finally
            {
                TMPTextPatch.KnownNpcNames.Remove("ZzzBookerTestThree");
            }
        }

        // NormalizeMaps / RestoreMaps

        [Fact]
        public void NormalizeMaps_ReplacesLowercaseWordBeforeNumberToken()
        {
            var withNumbers = TMPTextPatch.NormalizeNumbers("2 players in intro-3", out _);
            var result = TMPTextPatch.NormalizeMaps(withNumbers, out var capturedMaps);
            Assert.Equal("{n1} players in {map}-{n2}", result);
            Assert.Equal(new[] { "intro" }, capturedMaps);
        }

        [Fact]
        public void NormalizeMaps_NoDashNumberPattern_ReturnsUnchanged()
        {
            var result = TMPTextPatch.NormalizeMaps("no map pattern here", out var captured);
            Assert.Equal("no map pattern here", result);
            Assert.Empty(captured);
        }

        [Fact]
        public void RestoreMaps_PutsLiteralMapNameBack()
        {
            var restored = TMPTextPatch.RestoreMaps("{n1} jogadores em {map}-{n2}", new[] { "intro" });
            Assert.Equal("{n1} jogadores em intro-{n2}", restored);
        }

        // StripDialogCursor

        [Theory]
        [InlineData("Frogzards!_", "Frogzards!")]
        [InlineData("Hello!_", "Hello!")]
        public void StripDialogCursor_RemovesCursorAfterPunctuation(string input, string expected)
        {
            Assert.Equal(expected, TMPTextPatch.StripDialogCursor(input));
        }

        [Fact]
        public void StripDialogCursor_KeepsUsernameTrailingUnderscore()
        {
            // "Linck_" não é cursor de diálogo — é um nome de usuário real do
            // AQW. A regra só corta '_' quando precedido por pontuação.
            Assert.Equal("Linck_", TMPTextPatch.StripDialogCursor("Linck_"));
        }
    }
}
