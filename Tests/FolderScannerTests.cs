using System;
using System.IO;
using System.Linq;
using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class FolderScannerTests : IDisposable
    {
        private readonly string _dir;

        public FolderScannerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aqw_folderscanner_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void EmptyFolder_WithNoFiles_IsReturned()
        {
            // Caso exato do bug reportado: pasta criada por engano, sem
            // nenhum arquivo dentro, ainda precisa aparecer na árvore.
            Directory.CreateDirectory(Path.Combine(_dir, "maps", "pirates.json"));

            var result = FolderScanner.GetAllFolderPaths(_dir).ToList();

            Assert.Contains("maps", result);
            Assert.Contains("maps/pirates.json", result);
        }

        [Fact]
        public void FolderWithFiles_IsAlsoReturned()
        {
            // FileTreeBuilder.BuildRoot já reusa o nó quando o mesmo caminho
            // também vem de keysByFile — então não faz mal devolver TODAS as
            // pastas, não só as vazias.
            var ui = Path.Combine(_dir, "ui");
            Directory.CreateDirectory(ui);
            File.WriteAllText(Path.Combine(ui, "hud.json"), "{}");

            var result = FolderScanner.GetAllFolderPaths(_dir).ToList();

            Assert.Contains("ui", result);
        }

        [Fact]
        public void UnderscorePrefixedFolder_IsExcluded()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "_capture"));
            Directory.CreateDirectory(Path.Combine(_dir, "_migration_backup"));

            var result = FolderScanner.GetAllFolderPaths(_dir).ToList();

            Assert.DoesNotContain("_capture", result);
            Assert.DoesNotContain("_migration_backup", result);
        }

        [Fact]
        public void UnderscorePrefixedSubfolder_IsExcluded()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "maps", "_old"));

            var result = FolderScanner.GetAllFolderPaths(_dir).ToList();

            Assert.Contains("maps", result);
            Assert.DoesNotContain("maps/_old", result);
        }

        [Fact]
        public void NestedEmptyFolders_AllLevelsReturned()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "a", "b", "c"));

            var result = FolderScanner.GetAllFolderPaths(_dir).ToList();

            Assert.Contains("a", result);
            Assert.Contains("a/b", result);
            Assert.Contains("a/b/c", result);
        }

        [Fact]
        public void RootDoesNotExist_ReturnsEmpty()
        {
            var missing = Path.Combine(_dir, "does_not_exist");

            var result = FolderScanner.GetAllFolderPaths(missing).ToList();

            Assert.Empty(result);
        }

        [Fact]
        public void NoFolders_ReturnsEmpty()
        {
            var result = FolderScanner.GetAllFolderPaths(_dir).ToList();

            Assert.Empty(result);
        }
    }
}
