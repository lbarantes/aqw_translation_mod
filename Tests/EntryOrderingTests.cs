using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class EntryOrderingTests
    {
        [Fact]
        public void Priority_Missing_IsLowest()
        {
            Assert.Equal(0, EntryOrdering.Priority("", "Attack"));
        }

        [Fact]
        public void Priority_Ignored_IsMiddle()
        {
            Assert.Equal(1, EntryOrdering.Priority("Attack", "Attack"));
        }

        [Fact]
        public void Priority_Translated_IsHighest()
        {
            Assert.Equal(2, EntryOrdering.Priority("Ataque", "Attack"));
        }

        [Fact]
        public void Compare_MissingComesBeforeIgnoredAndTranslated()
        {
            // "Missing" < "Ignored" < "Translated", independente do texto.
            Assert.True(EntryOrdering.Compare("Zebra", "", "Apple", "Apple") < 0);
            Assert.True(EntryOrdering.Compare("Zebra", "", "Apple", "Maçã") < 0);
            Assert.True(EntryOrdering.Compare("Apple", "Apple", "Zebra", "Zebra_traduzido") < 0);
        }

        [Fact]
        public void Compare_SamePriority_FallsBackToAlphabeticalOrdinalIgnoreCase()
        {
            Assert.True(EntryOrdering.Compare("apple", "", "Banana", "") < 0);
            Assert.Equal(0, EntryOrdering.Compare("Apple", "", "apple", ""));
        }
    }
}
