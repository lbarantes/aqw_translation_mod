using System;
using System.IO;
using System.Linq;
using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class TreeNodeOpsTests : IDisposable
    {
        private readonly string _dir;

        public TreeNodeOpsTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aqw_treenodeops_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        private string Full(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));

        private void Touch(string rel)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Full(rel))!);
            File.WriteAllText(Full(rel), "{}");
        }

        // ValidateMove

        [Fact]
        public void ValidateMove_FileIntoOtherFolder_ReturnsDestination()
        {
            Touch("ui/hud.json");
            Directory.CreateDirectory(Full("maps"));

            var error = TreeNodeOps.ValidateMove(_dir, "ui/hud.json", false, "maps", out var destRel);

            Assert.Null(error);
            Assert.Equal("maps/hud.json", destRel);
        }

        [Fact]
        public void ValidateMove_FileToRoot_ReturnsBareName()
        {
            Touch("ui/hud.json");

            var error = TreeNodeOps.ValidateMove(_dir, "ui/hud.json", false, "", out var destRel);

            Assert.Null(error);
            Assert.Equal("hud.json", destRel);
        }

        [Fact]
        public void ValidateMove_SameFolder_IsRejected()
        {
            Touch("ui/hud.json");

            Assert.NotNull(TreeNodeOps.ValidateMove(_dir, "ui/hud.json", false, "ui", out _));
        }

        [Fact]
        public void ValidateMove_FolderIntoItselfOrDescendant_IsRejected()
        {
            Directory.CreateDirectory(Full("npcs/quest"));

            Assert.NotNull(TreeNodeOps.ValidateMove(_dir, "npcs", true, "npcs", out _));
            Assert.NotNull(TreeNodeOps.ValidateMove(_dir, "npcs", true, "npcs/quest", out _));
        }

        [Fact]
        public void ValidateMove_FolderIntoSiblingWithSamePrefix_IsAllowed()
        {
            Directory.CreateDirectory(Full("npcs"));
            Directory.CreateDirectory(Full("npcs2"));

            var error = TreeNodeOps.ValidateMove(_dir, "npcs", true, "npcs2", out var destRel);

            Assert.Null(error);
            Assert.Equal("npcs2/npcs", destRel);
        }

        [Fact]
        public void ValidateMove_ExistingDestination_IsRejected()
        {
            Touch("ui/hud.json");
            Touch("maps/hud.json");

            Assert.NotNull(TreeNodeOps.ValidateMove(_dir, "ui/hud.json", false, "maps", out _));
        }

        [Fact]
        public void ValidateMove_ReservedRootFiles_AreRejected()
        {
            Touch("uncategorized.json");
            Touch("misc/uncategorized.json");

            Assert.NotNull(TreeNodeOps.ValidateMove(_dir, "uncategorized.json", false, "misc2", out _));
            // arquivo comum com o mesmo nome não pode ir pra raiz
            Assert.NotNull(TreeNodeOps.ValidateMove(_dir, "misc/uncategorized.json", false, "", out _));
        }

        // Move

        [Fact]
        public void Move_Folder_MovesEverythingAndReportsJsonFiles()
        {
            Touch("npcs/alina.json");
            Touch("npcs/quest/x.json");
            Directory.CreateDirectory(Full("chars"));

            var moved = TreeNodeOps.Move(_dir, "npcs", true, "chars/npcs");

            Assert.False(Directory.Exists(Full("npcs")));
            Assert.True(File.Exists(Full("chars/npcs/alina.json")));
            Assert.True(File.Exists(Full("chars/npcs/quest/x.json")));
            Assert.Equal(2, moved.Count);
            Assert.Contains(moved, m => m.OldFull == Full("npcs/quest/x.json") && m.NewFull == Full("chars/npcs/quest/x.json"));
        }

        [Fact]
        public void Move_File_CreatesMissingDestinationFolder()
        {
            Touch("ui/hud.json");

            var moved = TreeNodeOps.Move(_dir, "ui/hud.json", false, "new/hud.json");

            Assert.True(File.Exists(Full("new/hud.json")));
            Assert.False(File.Exists(Full("ui/hud.json")));
            Assert.Single(moved);
        }

        // TrashFolder

        [Fact]
        public void TrashFolder_MovesIntoTrashInsteadOfDeleting()
        {
            Touch("maps/pirates.json");
            Touch("maps/sub/a.json");

            var (removed, trashRel) = TreeNodeOps.TrashFolder(_dir, "maps", new DateTime(2026, 10, 19, 13, 5, 0));

            Assert.False(Directory.Exists(Full("maps")));
            Assert.Equal("_trash/20261019_130500/maps", trashRel);
            Assert.True(File.Exists(Full("_trash/20261019_130500/maps/sub/a.json")));
            Assert.Equal(2, removed.Count);
            Assert.Contains(Full("maps/pirates.json"), removed);
        }

        [Fact]
        public void TrashFolder_SameTimestampTwice_DoesNotCollide()
        {
            var now = new DateTime(2026, 10, 19, 13, 5, 0);
            Touch("maps/a.json");
            TreeNodeOps.TrashFolder(_dir, "maps", now);
            Touch("maps/b.json");

            var (_, trashRel) = TreeNodeOps.TrashFolder(_dir, "maps", now);

            Assert.Equal("_trash/20261019_130500_2/maps", trashRel);
            Assert.True(File.Exists(Full("_trash/20261019_130500_2/maps/b.json")));
        }

        [Fact]
        public void TrashFolder_RootOrInternalFolder_Throws()
        {
            Directory.CreateDirectory(Full("_sync"));

            Assert.Throws<InvalidOperationException>(() => TreeNodeOps.TrashFolder(_dir, "", DateTime.Now));
            Assert.Throws<InvalidOperationException>(() => TreeNodeOps.TrashFolder(_dir, "_sync", DateTime.Now));
        }

        [Fact]
        public void CountContents_CountsJsonFilesAndSubfolders()
        {
            Touch("maps/a.json");
            Touch("maps/sub/b.json");
            File.WriteAllText(Full("maps/notes.txt"), "x");

            var (files, folders) = TreeNodeOps.CountContents(_dir, "maps");

            Assert.Equal(2, files);
            Assert.Equal(1, folders);
        }

        // NewNodeNameValidator — nomes reservados da raiz

        [Fact]
        public void ValidateFileName_UncategorizedAtRoot_IsRejected()
        {
            Assert.NotNull(NewNodeNameValidator.ValidateFileName("uncategorized", "", out _));
            Assert.NotNull(NewNodeNameValidator.ValidateFileName("Uncategorized.json", "", out _));
        }

        [Fact]
        public void ValidateFileName_UncategorizedInSubfolder_IsAllowed()
        {
            Assert.Null(NewNodeNameValidator.ValidateFileName("uncategorized", "misc", out var rel));
            Assert.Equal("misc/uncategorized.json", rel);
        }
    }
}
