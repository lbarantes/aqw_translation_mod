using AQWMod.Localization.Utils;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class RichTextUtilsTests
    {
        [Theory]
        [InlineData("<color=red>Attack</color>", true)]
        [InlineData("Plain text", false)]
        [InlineData("1 < 2", false)] // '<' presente mas não é uma tag reconhecida
        [InlineData("<b>bold</b>", true)]
        public void HasRichText_DetectsOnlyRecognizedTmpTags(string input, bool expected)
        {
            Assert.Equal(expected, RichTextUtils.HasRichText(input));
        }

        [Fact]
        public void StripAll_RemovesRecognizedTags_KeepsInnerText()
        {
            Assert.Equal("Attack", RichTextUtils.StripAll("<color=red>Attack</color>"));
        }

        [Fact]
        public void StripAll_LeavesPlainTextUnchanged()
        {
            Assert.Equal("Plain text", RichTextUtils.StripAll("Plain text"));
        }

        [Fact]
        public void StripAndCapture_ThenReinject_RoundTrips()
        {
            var original = "<color=red>Attack</color> now";
            var stripped = RichTextUtils.StripAndCapture(original, out var tags);

            Assert.DoesNotContain("<color", stripped);
            Assert.Equal(2, tags.Count); // <color=red> e </color>

            // Simula uma "tradução" que preserva os marcadores de posição.
            var reinjected = RichTextUtils.Reinject(stripped.Replace("Attack", "Ataque"), tags);
            Assert.Equal("<color=red>Ataque</color> now", reinjected);
        }

        [Fact]
        public void StripAndCapture_NoTags_ReturnsOriginalWithEmptyTagList()
        {
            var stripped = RichTextUtils.StripAndCapture("Plain text", out var tags);
            Assert.Equal("Plain text", stripped);
            Assert.Empty(tags);
        }

        [Fact]
        public void Reinject_EmptyTagList_ReturnsInputUnchanged()
        {
            Assert.Equal("Ataque now", RichTextUtils.Reinject("Ataque now", new System.Collections.Generic.List<string>()));
        }

        [Fact]
        public void MultipleTagsOfSameKind_PreserveOrderOnReinject()
        {
            var original = "<b>Bold</b> and <i>Italic</i>";
            var stripped = RichTextUtils.StripAndCapture(original, out var tags);
            var reinjected = RichTextUtils.Reinject(stripped, tags);
            Assert.Equal(original, reinjected);
        }
    }
}
