using System;
using System.IO;
using System.Linq;
using AQWMod.Localization.Repository;
using AQWMod.Localization.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class SyncEngineTests : IDisposable
    {
        private readonly string _root;
        private readonly string _local;
        private readonly string _remote;

        public SyncEngineTests()
        {
            _root   = Path.Combine(Path.GetTempPath(), "aqw_sync_tests_" + Guid.NewGuid().ToString("N"));
            _local  = Path.Combine(_root, "local");
            _remote = Path.Combine(_root, "remote");
            Directory.CreateDirectory(_local);
            Directory.CreateDirectory(_remote);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private static string Doc(params (string k, string v)[] entries)
        {
            var t = new JObject();
            foreach (var (k, v) in entries) t[k] = v;
            return new JObject { ["$meta"] = new JObject { ["version"] = "1" }, ["translations"] = t }.ToString();
        }

        private static void Write(string root, string rel, string json)
        {
            var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, json);
        }

        private static JObject Entries(string root, string rel) =>
            (JObject)JObject.Parse(File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))))["translations"]!;

        private SyncReport Run(bool dry = false) =>
            SyncEngine.Apply(new SyncOptions { LocalRoot = _local, RemoteRoot = _remote, PackageVersion = "1", DryRun = dry });

        [Fact]
        public void FirstSync_CreatesFile_AndBaseSnapshot()
        {
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            var rep = Run();

            Assert.Equal(FileSyncAction.Created, rep.Files.Single().Action);
            Assert.Equal("Jogar", (string)Entries(_local, "ui/hud.json")["Play"]!);
            Assert.True(File.Exists(Path.Combine(_local, "_sync", "base", "ui", "hud.json")));
            Assert.True(File.Exists(Path.Combine(_local, "_sync", "state.json")));
        }

        [Fact]
        public void SecondRun_WithNoChanges_IsNoOp()
        {
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            Run();
            var rep = Run();
            Assert.All(rep.Files, f => Assert.Equal(FileSyncAction.Unchanged, f.Action));
            Assert.Equal(0, rep.Changed);
        }

        [Fact]
        public void LocalEdit_Survives_UnrelatedRemoteUpdate()
        {
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar"), ("Quit", "Sair")));
            Run();

            // usuário edita Play; o GitHub muda Quit
            Write(_local, "ui/hud.json", Doc(("Play", "Começar"), ("Quit", "Sair")));
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar"), ("Quit", "Sair do jogo")));
            var rep = Run();

            var e = Entries(_local, "ui/hud.json");
            Assert.Equal("Começar", (string)e["Play"]!);
            Assert.Equal("Sair do jogo", (string)e["Quit"]!);
            Assert.Empty(rep.Conflicts);
        }

        [Fact]
        public void Conflict_RemoteWinsInFile_UserVersionGoesToOverrides_AndStillWinsAtRuntime()
        {
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            Run();

            Write(_local, "ui/hud.json", Doc(("Play", "Começar")));
            Write(_remote, "ui/hud.json", Doc(("Play", "Iniciar")));
            var rep = Run();

            Assert.Equal("Iniciar", (string)Entries(_local, "ui/hud.json")["Play"]!);
            Assert.Equal("Começar", (string)Entries(_local, "overrides/" + SyncEngine.ConflictsFile)["Play"]!);
            Assert.Equal(1, rep.OverridesWritten);

            // segunda rodada: sem novo conflito, override permanece
            var again = Run();
            Assert.Empty(again.Conflicts);
            Assert.Equal("Começar", (string)Entries(_local, "overrides/" + SyncEngine.ConflictsFile)["Play"]!);
        }

        [Fact]
        public void Conflict_NotDuplicated_WhenUserAlreadyHasOverrideForKey()
        {
            Write(_local, "uncategorized.json", Doc(("Play", "Meu Play")));
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            Run();
            Write(_local, "ui/hud.json", Doc(("Play", "Outro")));
            Write(_remote, "ui/hud.json", Doc(("Play", "Iniciar")));
            var rep = Run();

            Assert.Equal(0, rep.OverridesWritten);
            Assert.False(File.Exists(Path.Combine(_local, "overrides", SyncEngine.ConflictsFile)));
        }

        [Fact]
        public void RemoteDeletesFile_UnchangedLocal_IsDeleted()
        {
            Write(_remote, "maps/a.json", Doc(("X", "x")));
            Run();
            File.Delete(Path.Combine(_remote, "maps", "a.json"));
            var rep = Run();

            Assert.Equal(FileSyncAction.Deleted, rep.Files.Single(f => f.File == "maps/a.json").Action);
            Assert.False(File.Exists(Path.Combine(_local, "maps", "a.json")));
            Assert.False(File.Exists(Path.Combine(_local, "_sync", "base", "maps", "a.json")));
        }

        [Fact]
        public void RemoteDeletesFile_UserEditedEntry_KeptInOverrides()
        {
            Write(_remote, "maps/a.json", Doc(("X", "x")));
            Run();
            Write(_local, "maps/a.json", Doc(("X", "meu x")));
            File.Delete(Path.Combine(_remote, "maps", "a.json"));
            Run();

            Assert.False(File.Exists(Path.Combine(_local, "maps", "a.json")));
            Assert.Equal("meu x", (string)Entries(_local, "overrides/" + SyncEngine.ConflictsFile)["X"]!);
        }

        [Fact]
        public void LocalOnlyFiles_AreNeverTouched()
        {
            Write(_local, "classes/mine.json", Doc(("Q", "q")));
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            Run();
            Assert.Equal("q", (string)Entries(_local, "classes/mine.json")["Q"]!);
        }

        [Theory]
        [InlineData("overrides/evil.json")]
        [InlineData("uncategorized.json")]
        [InlineData("npc_names.json")]
        [InlineData("_capture/x.json")]
        public void RemoteFiles_InReservedLocations_AreRejected(string rel)
        {
            Write(_remote, rel, Doc(("A", "a")));
            var rep = Run();

            Assert.Equal(FileSyncAction.Skipped, rep.Files.Single(f => f.File == rel).Action);
            Assert.False(File.Exists(Path.Combine(_local, rel.Replace('/', Path.DirectorySeparatorChar))));
        }

        [Fact]
        public void MalformedRemoteFile_IsSkipped_OthersStillApply()
        {
            Write(_remote, "bad.json", "{ not json");
            Write(_remote, "ok.json", Doc(("A", "a")));
            var rep = Run();

            Assert.Equal(FileSyncAction.Skipped, rep.Files.Single(f => f.File == "bad.json").Action);
            Assert.Equal(FileSyncAction.Created, rep.Files.Single(f => f.File == "ok.json").Action);
            Assert.False(File.Exists(Path.Combine(_local, "bad.json")));
        }

        [Fact]
        public void MalformedLocalFile_IsNotOverwritten()
        {
            Write(_local, "ui/hud.json", "{ corrupted by hand");
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            var rep = Run();

            Assert.Equal(FileSyncAction.Skipped, rep.Files.Single().Action);
            Assert.Equal("{ corrupted by hand", File.ReadAllText(Path.Combine(_local, "ui", "hud.json")));
        }

        [Fact]
        public void DryRun_ReportsButWritesNothing()
        {
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            var rep = Run(dry: true);

            Assert.Equal(FileSyncAction.Created, rep.Files.Single().Action);
            Assert.False(Directory.Exists(Path.Combine(_local, "ui")));
            Assert.False(Directory.Exists(Path.Combine(_local, "_sync")));
        }

        [Fact]
        public void ManifestFile_IsNotTreatedAsTranslation()
        {
            Write(_remote, "_manifest.json", "{\"packageVersion\":\"1\"}");
            Write(_remote, "ui/hud.json", Doc(("Play", "Jogar")));
            var rep = Run();
            Assert.DoesNotContain(rep.Files, f => f.File == "_manifest.json");
        }

        [Fact]
        public void LegacyFlatFormat_IsPreserved()
        {
            Write(_remote, "flat.json", "{ \"A\": \"a\" }");
            Run();
            var root = JObject.Parse(File.ReadAllText(Path.Combine(_local, "flat.json")));
            Assert.Equal("a", (string)root["A"]!);
            Assert.Null(root["translations"]);
        }

        [Fact]
        public void HotReloadWatcher_IgnoresInternalFolders()
        {
            var rootPath = Path.Combine(_local);
            Assert.True(HotReloadWatcher.ShouldIgnore(rootPath, Path.Combine(rootPath, "_sync", "base", "ui", "hud.json")));
            Assert.True(HotReloadWatcher.ShouldIgnore(rootPath, Path.Combine(rootPath, "_capture", "ui.json")));
            Assert.False(HotReloadWatcher.ShouldIgnore(rootPath, Path.Combine(rootPath, "overrides", "sync_conflicts.json")));
            Assert.False(HotReloadWatcher.ShouldIgnore(rootPath, Path.Combine(rootPath, "ui", "hud.json")));
        }
    }
}
