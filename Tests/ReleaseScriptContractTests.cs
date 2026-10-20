using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // tools/make-release.ps1 e o validador do mod (RemoteManifest, PackageExtractor,
    // SyncEngine) são escritos separadamente: este teste garante que o que o script
    // gera é aceito pelo cliente — um pacote recusado só apareceria depois da
    // publicação do release.
    public class ReleaseScriptContractTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw_rel_" + Guid.NewGuid().ToString("N"));
        public ReleaseScriptContractTests() => Directory.CreateDirectory(_root);
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        private static string? FindRepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "tools", "make-release.ps1"))) d = d.Parent;
            return d?.FullName;
        }

        private static (int exit, string output) RunScript(string script, params string[] args)
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " +
                            string.Join(" ", args.Select(a => a.StartsWith("-") ? a : "\"" + a + "\""));
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, o);
        }

        private static void Put(string root, string rel, string content)
        {
            var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content, new System.Text.UTF8Encoding(false));
        }

        private (RemoteManifest manifest, string zip, string pkgDir) Release(string source, string version = "2026.10.12")
        {
            var repo = FindRepoRoot();
            Assert.NotNull(repo);
            var outDir = Path.Combine(_root, "out");
            var (exit, output) = RunScript(Path.Combine(repo!, "tools", "make-release.ps1"),
                "-Source", source, "-Out", outDir, "-Version", version);
            Assert.True(exit == 0, output);

            var json = File.ReadAllText(Path.Combine(outDir, "manifest.json"));
            Assert.True(RemoteManifest.TryParse(json, new SyncLimits(), out var m, out var err), err);

            var zip = Path.Combine(outDir, "translations.zip");
            PackageExtractor.VerifyPackageFile(zip, m);
            var pkg = Path.Combine(_root, "pkg");
            PackageExtractor.ExtractVerified(zip, m, pkg, new SyncLimits());
            return (m, zip, pkg);
        }

        [Fact]
        public void GeneratedPackage_IsAcceptedByTheClientValidator()
        {
            var src = Path.Combine(_root, "src");
            Put(src, "ui/hud.json", "{\"translations\":{\"Play\":\"Jogar\"}}");
            Put(src, "maps/Pirates/rhuibarb.json", "{\"translations\":{\"A\":\"á\"}}");
            Put(src, "generics.json", "{\"Quit\":\"Sair\"}");
            // tudo isto precisa ficar de fora
            Put(src, "_capture/ui.json", "{}");
            Put(src, "overrides/mine.json", "{}");
            Put(src, "uncategorized.json", "{}");
            Put(src, "npc_names.json", "{}");
            Put(src, "ui/hud.json.bak", "x");
            Put(src, "notes.txt", "x");

            var (m, _, pkg) = Release(src);

            Assert.Equal("2026.10.12", m.PackageVersion.Raw);
            Assert.Equal(new[] { "generics.json", "maps/Pirates/rhuibarb.json", "ui/hud.json" },
                m.Files.Select(f => f.Path).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.Equal("á", (string)JObject.Parse(File.ReadAllText(Path.Combine(pkg, "maps", "Pirates", "rhuibarb.json")))["translations"]!["A"]!);
        }

        [Fact]
        public void KeysThatDifferOnlyByCase_AreNotRejected()
        {
            var src = Path.Combine(_root, "src");
            Put(src, "items/armor.json", "{\"translations\":{\"Leg Armor!\":\"a\",\"Leg armor!\":\"b\"}}");
            var (m, _, _) = Release(src);
            Assert.Single(m.Files);
        }

        [Fact]
        public void InvalidJson_AbortsTheRelease()
        {
            var src = Path.Combine(_root, "src");
            Put(src, "ui/broken.json", "{ nope");
            var repo = FindRepoRoot()!;
            var (exit, _) = RunScript(Path.Combine(repo, "tools", "make-release.ps1"),
                "-Source", src, "-Out", Path.Combine(_root, "out"), "-Version", "2026.10.12");
            Assert.NotEqual(0, exit);
        }

        [Fact]
        public async Task GeneratedPackage_InstallsThroughTheWholeUpdatePipeline()
        {
            var src = Path.Combine(_root, "src");
            Put(src, "ui/hud.json", "{\"translations\":{\"Play\":\"Jogar\"}}");
            var (_, zip, _) = Release(src);

            var manifestJson = File.ReadAllText(Path.Combine(_root, "out", "manifest.json"));
            var local = Path.Combine(_root, "local");
            Directory.CreateDirectory(local);

            var result = await UpdateCoordinator.RunAsync(
                new UpdateOptions { LocalRoot = local, ModVersion = "2.2.0" },
                new FakeReleaseSource { ManifestJson = manifestJson, ZipPath = zip }, CancellationToken.None);

            Assert.Equal(UpdateStatus.Updated, result.Status);
            Assert.Equal("Jogar", (string)JObject.Parse(File.ReadAllText(Path.Combine(local, "ui", "hud.json")))["translations"]!["Play"]!);
        }

        [Fact]
        public void RepositoryTranslations_ProduceAValidRelease()
        {
            var repo = FindRepoRoot();
            var real = repo == null ? null : Path.Combine(repo, "translations");
            if (real == null || !Directory.Exists(real) || !Directory.GetFiles(real, "*.json", SearchOption.AllDirectories).Any())
                return; // nada versionado ainda

            var (m, _, _) = Release(real);
            Assert.NotEmpty(m.Files);
        }

        // tools/sign-mod-release.ps1 monta o texto assinado à mão; se ele
        // divergir de ModRelease.SignedPayload, todo jogador recusaria a
        // atualização do mod — só apareceria depois do release publicado
        [Fact]
        public void SignedModSection_IsAcceptedByTheMod()
        {
            var repo = FindRepoRoot();
            Assert.NotNull(repo);

            using var rsa = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
            var keyPath = Path.Combine(_root, "key.xml");
            File.WriteAllText(keyPath, rsa.ToXmlString(true));
            var player = Path.Combine(_root, "player.dll");
            var translator = Path.Combine(_root, "translator.dll");
            File.WriteAllBytes(player, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(translator, new byte[] { 4, 5, 6, 7 });
            var modJson = Path.Combine(_root, "mod.json");

            var (exit, output) = RunScript(Path.Combine(repo!, "tools", "sign-mod-release.ps1"),
                "-Version", "0.2.0", "-PlayerDll", player, "-TranslatorDll", translator, "-Out", modJson, "-KeyPath", keyPath);
            Assert.True(exit == 0, output);

            var src = Path.Combine(_root, "src");
            Put(src, "ui/hud.json", "{\"translations\":{\"Play\":\"Jogar\"}}");
            var outDir = Path.Combine(_root, "out");
            (exit, output) = RunScript(Path.Combine(repo!, "tools", "make-release.ps1"),
                "-Source", src, "-Out", outDir, "-Version", "0.2.0", "-ModInfoFile", modJson);
            Assert.True(exit == 0, output);

            Assert.True(RemoteManifest.TryParse(File.ReadAllText(Path.Combine(outDir, "manifest.json")),
                new SyncLimits(), out var m, out var err), err);
            Assert.NotNull(m.Mod);
            Assert.Equal("0.2.0", m.Mod!.Version.Raw);
            Assert.Equal(4, m.Mod.Editions["Translator"].SizeBytes);
            Assert.True(m.Mod.VerifySignature(rsa.ToXmlString(false)));
        }
    }
}
