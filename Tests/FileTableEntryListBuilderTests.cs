using System;
using System.IO;
using System.Linq;
using AQWMod.Localization.Overlay;
using AQWMod.Localization.Repository;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class FileTableEntryListBuilderTests : IDisposable
    {
        private readonly string _dir;

        public FileTableEntryListBuilderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aqw_filetable_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        private string PathFor(string name) => Path.Combine(_dir, name);

        [Fact]
        public void Build_NonExistentFile_ReturnsEmptyList()
        {
            var result = FileTableEntryListBuilder.Build("hud.json", PathFor("hud.json"));
            Assert.Empty(result);
        }

        [Fact]
        public void Build_SimpleEntries_MapsKeyAndSavedCorrectly()
        {
            var file = PathFor("hud.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");

            var result = FileTableEntryListBuilder.Build("ui/hud.json", file);

            var e = Assert.Single(result);
            Assert.Equal("Attack", e.key);
            Assert.Equal("Ataque", e.saved);
            Assert.Equal(file, e.sourceFile);
            Assert.Equal("ui/hud.json", e.path);
        }

        [Fact]
        public void Build_IgnoredEntry_SavedEqualsKey()
        {
            var file = PathFor("hud.json");
            TranslationFileStore.Upsert(file, "OK", "irrelevante", ignore: true);

            var e = Assert.Single(FileTableEntryListBuilder.Build("hud.json", file));
            Assert.Equal("OK", e.saved);
        }

        [Fact]
        public void Build_MultipleEntries_AreOrderedByEntryOrdering()
        {
            var file = PathFor("hud.json");
            TranslationFileStore.Upsert(file, "ZIgnored", "ZIgnored", ignore: true);
            TranslationFileStore.Upsert(file, "AIgnored", "AIgnored", ignore: true);
            TranslationFileStore.Upsert(file, "AWord", "Traduzido");

            var keys = FileTableEntryListBuilder.Build("hud.json", file).Select(e => e.key).ToList();

            Assert.Equal(new[] { "AIgnored", "ZIgnored", "AWord" }, keys);
        }
    }
}
