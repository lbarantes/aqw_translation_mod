using System.Collections.Generic;
using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class FileTreeBuilderTests
    {
        private static Dictionary<string, IReadOnlyList<string>> KeysByFile(
            params (string file, string[] keys)[] entries)
        {
            var dict = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var (file, keys) in entries) dict[file] = keys;
            return dict;
        }

        [Fact]
        public void SingleFileAtRoot_BecomesDirectChildOfRoot()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("hud.json", new[] { "Attack" })));

            var file = Assert.Single(root.Children);
            Assert.True(file.IsFile);
            Assert.Equal("hud.json", file.Name);
            Assert.Equal("hud.json", file.RelativePath);
            Assert.Equal(1, file.EntryCount);
        }

        [Fact]
        public void FileInsideFolder_CreatesFolderNode()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("ui/hud.json", new[] { "Attack", "Defend" })));

            var folder = Assert.Single(root.Children);
            Assert.False(folder.IsFile);
            Assert.Equal("ui", folder.Name);

            var file = Assert.Single(folder.Children);
            Assert.True(file.IsFile);
            Assert.Equal("hud.json", file.Name);
            Assert.Equal("ui/hud.json", file.RelativePath);
            Assert.Equal(2, file.EntryCount);
        }

        [Fact]
        public void DeeplyNestedPath_CreatesEachIntermediateFolder()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("maps/pirates/rhuibarb.json", new[] { "X" })));

            var maps = Assert.Single(root.Children);
            Assert.Equal("maps", maps.Name);
            var pirates = Assert.Single(maps.Children);
            Assert.Equal("pirates", pirates.Name);
            var file = Assert.Single(pirates.Children);
            Assert.Equal("rhuibarb.json", file.Name);
            Assert.Equal("maps/pirates/rhuibarb.json", file.RelativePath);
        }

        [Fact]
        public void MultipleFilesShareSameFolder_FolderNodeIsNotDuplicated()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(
                ("ui/hud.json", new[] { "Attack" }),
                ("ui/menu.json", new[] { "Close" })));

            var folder = Assert.Single(root.Children);
            Assert.Equal("ui", folder.Name);
            Assert.Equal(2, folder.Children.Count);
        }

        [Fact]
        public void BackslashSeparator_IsTreatedTheSameAsForwardSlash()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("ui\\hud.json", new[] { "Attack" })));

            var folder = Assert.Single(root.Children);
            Assert.Equal("ui", folder.Name);
            var file = Assert.Single(folder.Children);
            Assert.Equal("hud.json", file.Name);
            Assert.Equal("ui/hud.json", file.RelativePath); // normalizado com '/'
        }

        [Fact]
        public void Children_AreSorted_FoldersBeforeFiles_ThenAlphabetically()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(
                ("zzz.json", new[] { "A" }),
                ("aaa.json", new[] { "B" }),
                ("mfolder/inner.json", new[] { "C" })));

            Assert.Equal(3, root.Children.Count);
            Assert.False(root.Children[0].IsFile); // pasta "mfolder" vem primeiro
            Assert.Equal("mfolder", root.Children[0].Name);
            Assert.True(root.Children[1].IsFile);
            Assert.Equal("aaa.json", root.Children[1].Name); // depois, alfabético
            Assert.True(root.Children[2].IsFile);
            Assert.Equal("zzz.json", root.Children[2].Name);
        }

        [Fact]
        public void EmptyInput_ProducesRootWithNoChildren()
        {
            var root = FileTreeBuilder.BuildRoot(new Dictionary<string, IReadOnlyList<string>>());
            Assert.Empty(root.Children);
        }

        // extraFolders (Etapa 4 — pastas criadas pela GUI sem arquivos)

        [Fact]
        public void ExtraFolder_WithNoFiles_AppearsInTree()
        {
            var root = FileTreeBuilder.BuildRoot(
                new Dictionary<string, IReadOnlyList<string>>(),
                extraFolders: new[] { "npcs" });

            var folder = Assert.Single(root.Children);
            Assert.False(folder.IsFile);
            Assert.Equal("npcs", folder.Name);
            Assert.Empty(folder.Children);
        }

        [Fact]
        public void ExtraFolder_Nested_CreatesEachIntermediateFolder()
        {
            var root = FileTreeBuilder.BuildRoot(
                new Dictionary<string, IReadOnlyList<string>>(),
                extraFolders: new[] { "npcs/quest" });

            var npcs = Assert.Single(root.Children);
            Assert.Equal("npcs", npcs.Name);
            var quest = Assert.Single(npcs.Children);
            Assert.Equal("quest", quest.Name);
            Assert.Empty(quest.Children);
        }

        [Fact]
        public void ExtraFolder_AlreadyHasFiles_DoesNotDuplicateFolderNode()
        {
            var root = FileTreeBuilder.BuildRoot(
                KeysByFile(("ui/hud.json", new[] { "Attack" })),
                extraFolders: new[] { "ui" });

            var folder = Assert.Single(root.Children);
            Assert.Equal("ui", folder.Name);
            Assert.Single(folder.Children); // continua só o hud.json, sem pasta duplicada
        }

        [Fact]
        public void ExtraFolder_Null_BehavesLikeNoExtraFolders()
        {
            var root = FileTreeBuilder.BuildRoot(
                KeysByFile(("hud.json", new[] { "Attack" })),
                extraFolders: null);

            Assert.Single(root.Children);
        }

        // Filter (2026-09-22 — busca da árvore filtra arquivo/pasta)

        [Fact]
        public void Filter_EmptyQuery_ReturnsSameTree()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("hud.json", new[] { "Attack" })));
            var filtered = FileTreeBuilder.Filter(root, "");
            Assert.Same(root, filtered);
        }

        [Fact]
        public void Filter_MatchingFileName_Survives()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(
                ("hud.json", new[] { "A" }),
                ("menu.json", new[] { "B" })));

            var filtered = FileTreeBuilder.Filter(root, "hud");

            var file = Assert.Single(filtered.Children);
            Assert.Equal("hud.json", file.Name);
        }

        [Fact]
        public void Filter_MatchingFolderName_KeepsAllOriginalChildren()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(
                ("guis/a.json", new[] { "A" }),
                ("guis/b.json", new[] { "B" })));

            var filtered = FileTreeBuilder.Filter(root, "guis");

            var folder = Assert.Single(filtered.Children);
            Assert.Equal(2, folder.Children.Count);
        }

        [Fact]
        public void Filter_DeepMatch_KeepsAncestorFoldersButPrunesUnrelatedSiblings()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(
                ("maps/pirates/rhuibarb.json", new[] { "X" }),
                ("maps/oaklore.json", new[] { "Y" })));

            var filtered = FileTreeBuilder.Filter(root, "rhuibarb");

            var maps = Assert.Single(filtered.Children);
            Assert.Equal("maps", maps.Name);
            var pirates = Assert.Single(maps.Children); // oaklore.json podado
            Assert.Equal("pirates", pirates.Name);
            Assert.Single(pirates.Children);
        }

        [Fact]
        public void Filter_NoMatch_ReturnsEmptyRoot()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("hud.json", new[] { "A" })));
            var filtered = FileTreeBuilder.Filter(root, "nonexistent");
            Assert.Empty(filtered.Children);
        }

        [Fact]
        public void Filter_IsCaseInsensitive()
        {
            var root = FileTreeBuilder.BuildRoot(KeysByFile(("HUD.json", new[] { "A" })));
            var filtered = FileTreeBuilder.Filter(root, "hud");
            Assert.Single(filtered.Children);
        }
    }
}
