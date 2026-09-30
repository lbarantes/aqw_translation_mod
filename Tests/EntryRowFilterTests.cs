using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class EntryRowFilterTests
    {
        [Fact]
        public void NoFilters_MatchesEverything()
        {
            var filter = new EntryRowFilter();
            Assert.True(filter.Matches("Attack", "Ataque", "", isTranslated: true, isIgnored: false, category: "ui", searchText: ""));
        }

        [Fact]
        public void UntranslatedOnly_HidesTranslatedRows()
        {
            var filter = new EntryRowFilter { UntranslatedOnly = true };
            Assert.False(filter.Matches("Attack", "Ataque", "", isTranslated: true, isIgnored: false, category: "ui", searchText: ""));
        }

        [Fact]
        public void UntranslatedOnly_HidesIgnoredRows()
        {
            // Ignorado != "faltando" — não deve reaparecer no filtro "sem tradução".
            var filter = new EntryRowFilter { UntranslatedOnly = true };
            Assert.False(filter.Matches("OK", "OK", "", isTranslated: false, isIgnored: true, category: "ui", searchText: ""));
        }

        [Fact]
        public void UntranslatedOnly_KeepsRowsThatAreNeitherTranslatedNorIgnored()
        {
            var filter = new EntryRowFilter { UntranslatedOnly = true };
            Assert.True(filter.Matches("Missing", "", "", isTranslated: false, isIgnored: false, category: "ui", searchText: ""));
        }

        [Theory]
        [InlineData("attack", true)]   // case-insensitive
        [InlineData("ATTACK", true)]
        [InlineData("xyz", false)]
        public void SearchText_MatchesOriginal_CaseInsensitive(string search, bool expected)
        {
            var filter = new EntryRowFilter();
            Assert.Equal(expected, filter.Matches("Attack", "Ataque", "", true, false, "ui", search));
        }

        [Fact]
        public void SearchText_AlsoMatchesTranslationBuffer()
        {
            var filter = new EntryRowFilter();
            Assert.True(filter.Matches("Attack", "Ataque", "", true, false, "ui", "ataque"));
        }

        [Fact]
        public void SearchText_AlsoMatchesRawOriginal_WhenNonEmpty()
        {
            var filter = new EntryRowFilter();
            Assert.True(filter.Matches("{player} joined", "{player} entrou", "Linck_ joined", true, false, "ui", "linck"));
        }

        [Fact]
        public void SearchText_IgnoresEmptyRawOriginal()
        {
            var filter = new EntryRowFilter();
            // rawOriginal vazio não deve gerar match nenhum por si só.
            Assert.False(filter.Matches("Attack", "Ataque", "", true, false, "ui", "zzz"));
        }

        [Fact]
        public void SelectedCategories_Empty_MeansAllCategoriesPass()
        {
            var filter = new EntryRowFilter();
            Assert.True(filter.Matches("Attack", "Ataque", "", true, false, "guis", ""));
        }

        [Fact]
        public void SelectedCategories_NonEmpty_OnlyMatchingCategoryPasses()
        {
            var filter = new EntryRowFilter();
            filter.SelectedCategories.Add("ui");

            Assert.True(filter.Matches("Attack", "Ataque", "", true, false, "ui", ""));
            Assert.False(filter.Matches("Attack", "Ataque", "", true, false, "guis", ""));
        }

        [Fact]
        public void AllFiltersCombine_WithAndSemantics()
        {
            var filter = new EntryRowFilter { UntranslatedOnly = true };
            filter.SelectedCategories.Add("ui");

            // Passa categoria, mas falha "sem tradução" (já traduzido).
            Assert.False(filter.Matches("Attack", "Ataque", "", true, false, "ui", ""));
            // Passa "sem tradução", mas falha categoria.
            Assert.False(filter.Matches("Missing", "", "", false, false, "guis", ""));
            // Passa os dois.
            Assert.True(filter.Matches("Missing", "", "", false, false, "ui", ""));
        }
    }
}
