using System;
using AQWMod.Localization.Interceptors;
using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class PlaceholderHintFormatterTests
    {
        [Fact]
        public void NoCapturedValues_ReturnsPathUnchanged()
        {
            var result = PlaceholderHintFormatter.Append("some/path", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Assert.Equal("some/path", result);
        }

        [Fact]
        public void PlayerCaptures_AppendOneLinePerPlayer_NumberedFromSecond()
        {
            var result = PlaceholderHintFormatter.Append("path", new[] { "Linck_", "Hero_AQW" },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            Assert.Equal("path\n{player} = \"Linck_\"\n{player2} = \"Hero_AQW\"", result);
        }

        [Fact]
        public void NumberCaptures_AppendOneLinePerNumber_AlwaysNumberedFromOne()
        {
            var result = PlaceholderHintFormatter.Append("path", Array.Empty<string>(),
                Array.Empty<string>(), new[] { "1", "30" }, Array.Empty<string>());
            Assert.Equal("path\n{n1} = 1\n{n2} = 30", result);
        }

        [Fact]
        public void MapCaptures_AppendOneLinePerMap()
        {
            var result = PlaceholderHintFormatter.Append("path", Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>(), new[] { "intro" });
            Assert.Equal("path\n{map} = \"intro\"", result);
        }

        [Fact]
        public void NpcCapture_WithNoKnownTranslation_ShowsOriginalNameOnly()
        {
            var result = PlaceholderHintFormatter.Append("path", Array.Empty<string>(),
                new[] { "ZzzHintFormatterNpcUnknown" }, Array.Empty<string>(), Array.Empty<string>());
            Assert.Equal("path\n{npc} = \"ZzzHintFormatterNpcUnknown\"", result);
        }

        [Fact]
        public void NpcCapture_WithKnownTranslation_ShowsOriginalArrowTranslated()
        {
            TMPTextPatch.KnownNpcNames["ZzzHintFormatterNpcKnown"] = "Bibliotecário";
            try
            {
                var result = PlaceholderHintFormatter.Append("path", Array.Empty<string>(),
                    new[] { "ZzzHintFormatterNpcKnown" }, Array.Empty<string>(), Array.Empty<string>());
                Assert.Equal("path\n{npc} = \"ZzzHintFormatterNpcKnown\" → \"Bibliotecário\"", result);
            }
            finally
            {
                TMPTextPatch.KnownNpcNames.Remove("ZzzHintFormatterNpcKnown");
            }
        }

        [Fact]
        public void AllCaptureTypesTogether_AppendInFixedOrder_PlayersNpcsNumbersMaps()
        {
            var result = PlaceholderHintFormatter.Append("path",
                players: new[] { "Linck_" },
                npcs: new[] { "ZzzHintFormatterOrderNpc" },
                nums: new[] { "5" },
                maps: new[] { "intro" });

            Assert.Equal(
                "path\n{player} = \"Linck_\"\n{npc} = \"ZzzHintFormatterOrderNpc\"\n{n1} = 5\n{map} = \"intro\"",
                result);
        }
    }
}
