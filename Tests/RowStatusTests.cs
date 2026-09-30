using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class RowStatusTests
    {
        [Fact]
        public void SavedDifferentFromOriginal_IsTranslated()
        {
            var status = RowStatus.Compute("Attack", "Ataque");
            Assert.Equal(RowStatusKind.Translated, status.Kind);
            Assert.Equal("[T]", status.Label);
        }

        [Fact]
        public void SavedEqualsOriginal_IsIgnored()
        {
            var status = RowStatus.Compute("OK", "OK");
            Assert.Equal(RowStatusKind.Ignored, status.Kind);
            Assert.Equal("[~]", status.Label);
        }

        [Fact]
        public void EmptySaved_IsMissing()
        {
            var status = RowStatus.Compute("Attack", "");
            Assert.Equal(RowStatusKind.Missing, status.Kind);
            Assert.Equal("[ ]", status.Label);
        }

        [Fact]
        public void NullSaved_IsMissing()
        {
            var status = RowStatus.Compute("Attack", null!);
            Assert.Equal(RowStatusKind.Missing, status.Kind);
        }
    }
}
