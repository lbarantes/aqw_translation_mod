using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // chave de teste gerada uma vez por execução — a de produção nunca entra nos testes
    internal static class TestModKey
    {
        private static readonly Lazy<RSACryptoServiceProvider> Rsa = new Lazy<RSACryptoServiceProvider>(() => new RSACryptoServiceProvider(2048));
        public static string PublicXml => Rsa.Value.ToXmlString(false);

        public static string Sign(string payload) =>
            Convert.ToBase64String(Rsa.Value.SignData(new UTF8Encoding(false).GetBytes(payload), "SHA256"));

        /// <summary>Seção "mod" assinada, com uma DLL falsa por edição.</summary>
        public static JObject ModSection(string version, Dictionary<string, byte[]> dlls, Func<string, string>? tamperPayload = null)
        {
            var editions = dlls.ToDictionary(d => d.Key, d => new ModEditionAsset
            {
                Name = $"AQWTranslation-{d.Key}.dll",
                Sha256 = TestPackage.Sha(d.Value),
                SizeBytes = d.Value.Length,
            });
            var payload = ModRelease.SignedPayload(version, editions);
            var eds = new JObject();
            foreach (var e in editions)
                eds[e.Key] = new JObject { ["name"] = e.Value.Name, ["sha256"] = e.Value.Sha256, ["sizeBytes"] = e.Value.SizeBytes };
            return new JObject
            {
                ["version"] = version,
                ["editions"] = eds,
                ["signature"] = Sign(tamperPayload?.Invoke(payload) ?? payload),
            };
        }
    }

    public class ModReleaseTests
    {
        private static readonly Dictionary<string, byte[]> Dlls = new Dictionary<string, byte[]>
        {
            ["Player"] = Encoding.ASCII.GetBytes("player-dll"),
            ["Translator"] = Encoding.ASCII.GetBytes("translator-dll"),
        };

        [Fact]
        public void SignedSection_ParsesAndVerifies()
        {
            Assert.True(ModRelease.TryParse(TestModKey.ModSection("0.2.0", Dlls), out var m, out _));
            Assert.Equal("0.2.0", m.Version.Raw);
            Assert.True(m.VerifySignature(TestModKey.PublicXml));
        }

        [Fact]
        public void ChangedHash_BreaksSignature()
        {
            var node = TestModKey.ModSection("0.2.0", Dlls);
            node["editions"]!["Player"]!["sha256"] = new string('b', 64);
            Assert.True(ModRelease.TryParse(node, out var m, out _));
            Assert.False(m.VerifySignature(TestModKey.PublicXml));
        }

        [Fact]
        public void ChangedVersion_BreaksSignature()
        {
            var node = TestModKey.ModSection("0.2.0", Dlls);
            node["version"] = "9.9.9";
            Assert.True(ModRelease.TryParse(node, out var m, out _));
            Assert.False(m.VerifySignature(TestModKey.PublicXml));
        }

        [Fact]
        public void OtherKey_IsRejected()
        {
            Assert.True(ModRelease.TryParse(TestModKey.ModSection("0.2.0", Dlls), out var m, out _));
            using var other = new RSACryptoServiceProvider(2048);
            Assert.False(m.VerifySignature(other.ToXmlString(false)));
            Assert.False(m.VerifySignature(ModSignature.PublicKeyXml));
        }

        [Theory]
        [InlineData("name", "../evil.dll")]
        [InlineData("name", "AQWTranslation-Player.exe")]
        [InlineData("sha256", "zz")]
        public void BadEditionFields_AreRejected(string field, string value)
        {
            var node = TestModKey.ModSection("0.2.0", Dlls);
            node["editions"]!["Player"]![field] = value;
            Assert.False(ModRelease.TryParse(node, out _, out _));
        }

        [Fact]
        public void InvalidModSection_DoesNotBreakTheTranslationsManifest()
        {
            var (_, manifestJson) = TestPackage.Build(Path.Combine(Path.GetTempPath(), "aqw_modsec_" + Guid.NewGuid().ToString("N")),
                "0.1.3", new[] { ("ui/hud.json", TestPackage.TranslationsJson(("Play", "Jogar"))) });
            var root = JObject.Parse(manifestJson);
            root["mod"] = new JObject { ["version"] = "x" };

            Assert.True(RemoteManifest.TryParse(root.ToString(), new SyncLimits(), out var m, out _));
            Assert.Null(m.Mod);
            Assert.NotNull(m.ModError);
        }

        [Fact]
        public void ProductionPublicKey_IsLoadable()
        {
            using var rsa = new RSACryptoServiceProvider();
            rsa.FromXmlString(ModSignature.PublicKeyXml);
            Assert.True(rsa.KeySize >= 3072);
        }
    }

    public class ModUpdaterTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw_modupd_" + Guid.NewGuid().ToString("N"));
        private string Plugins => Path.Combine(_root, "plugins");
        private string Dll => Path.Combine(Plugins, "AQWTranslation.dll");
        private string Local => Path.Combine(_root, "translations");
        private string Pkg => Path.Combine(_root, "pkg");

        private static readonly byte[] OldDll = Encoding.ASCII.GetBytes("old-player");
        private static readonly byte[] NewPlayer = Encoding.ASCII.GetBytes("new-player-0.2.0");
        private static readonly byte[] NewTranslator = Encoding.ASCII.GetBytes("new-translator-0.2.0");

        public ModUpdaterTests()
        {
            Directory.CreateDirectory(Plugins);
            Directory.CreateDirectory(Local);
            File.WriteAllBytes(Dll, OldDll);
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        private FakeReleaseSource Release(string modVersion, Func<string, string>? tamperPayload = null,
            byte[]? servedPlayer = null, bool withTranslator = true)
        {
            var dlls = new Dictionary<string, byte[]> { ["Player"] = NewPlayer };
            if (withTranslator) dlls["Translator"] = NewTranslator;

            var (zip, manifestJson) = TestPackage.Build(Pkg, "0.1.3",
                new[] { ("ui/hud.json", TestPackage.TranslationsJson(("Play", "Jogar"))) });
            var manifest = JObject.Parse(manifestJson);
            manifest["mod"] = TestModKey.ModSection(modVersion, dlls, tamperPayload);

            var src = new FakeReleaseSource { ManifestJson = manifest.ToString(), ZipPath = zip };
            foreach (var d in dlls)
            {
                var path = Path.Combine(Pkg, $"AQWTranslation-{d.Key}.dll");
                File.WriteAllBytes(path, d.Key == "Player" && servedPlayer != null ? servedPlayer : d.Value);
                src.Assets[$"AQWTranslation-{d.Key}.dll"] = path;
            }
            return src;
        }

        private Task<UpdateResult> Run(FakeReleaseSource src, string modVersion = "0.1.0", string edition = "Player") =>
            UpdateCoordinator.RunAsync(new UpdateOptions
            {
                LocalRoot = Local,
                ModVersion = modVersion,
                Mod = new ModUpdateOptions { DllPath = Dll, Edition = edition, PublicKeyXml = TestModKey.PublicXml },
            }, src, CancellationToken.None);

        [Fact]
        public async Task NewerSignedMod_IsSwappedIn_AndTranslationsStillApply()
        {
            var r = await Run(Release("0.2.0"));

            Assert.Equal(ModUpdateStatus.Staged, r.ModUpdate!.Status);
            Assert.Equal(NewPlayer, File.ReadAllBytes(Dll));
            Assert.Equal(OldDll, File.ReadAllBytes(Dll + ModUpdater.PreviousSuffix));
            Assert.Equal(UpdateStatus.Updated, r.Status);
            Assert.False(Directory.Exists(Path.Combine(Local, "_sync", "staging_mod")));
        }

        [Fact]
        public async Task TranslatorEdition_GetsTranslatorDll()
        {
            var r = await Run(Release("0.2.0"), edition: "Translator");
            Assert.Equal(ModUpdateStatus.Staged, r.ModUpdate!.Status);
            Assert.Equal(NewTranslator, File.ReadAllBytes(Dll));
        }

        [Theory]
        [InlineData("0.2.0")]
        [InlineData("0.3.0")]
        public async Task SameOrOlderVersion_IsLeftAlone(string installed)
        {
            var r = await Run(Release("0.2.0"), modVersion: installed);
            Assert.Equal(ModUpdateStatus.UpToDate, r.ModUpdate!.Status);
            Assert.Equal(OldDll, File.ReadAllBytes(Dll));
        }

        [Fact]
        public async Task BadSignature_IsRejected_WithoutDownloading()
        {
            var src = Release("0.2.0", tamperPayload: p => p.Replace("0.2.0", "0.2.1"));
            var r = await Run(src);

            Assert.Equal(ModUpdateStatus.Rejected, r.ModUpdate!.Status);
            Assert.Equal(OldDll, File.ReadAllBytes(Dll));
            Assert.Equal(UpdateStatus.Updated, r.Status); // traduções seguem
        }

        [Fact]
        public async Task ServedDllDifferentFromSigned_IsRejected()
        {
            var r = await Run(Release("0.2.0", servedPlayer: Encoding.ASCII.GetBytes("evil-player-0.2.0")));
            Assert.Equal(ModUpdateStatus.Rejected, r.ModUpdate!.Status);
            Assert.Equal(OldDll, File.ReadAllBytes(Dll));
            Assert.False(File.Exists(Dll + ModUpdater.PreviousSuffix));
        }

        [Fact]
        public async Task MissingEdition_IsRejected()
        {
            var r = await Run(Release("0.2.0", withTranslator: false), edition: "Translator");
            Assert.Equal(ModUpdateStatus.Rejected, r.ModUpdate!.Status);
            Assert.Equal(OldDll, File.ReadAllBytes(Dll));
        }

        [Fact]
        public async Task WithoutModOptions_ModIsNotTouched()
        {
            var r = await UpdateCoordinator.RunAsync(new UpdateOptions { LocalRoot = Local, ModVersion = "0.1.0" },
                Release("0.2.0"), CancellationToken.None);
            Assert.Null(r.ModUpdate);
            Assert.Equal(OldDll, File.ReadAllBytes(Dll));
        }

        [Fact]
        public void CleanupPrevious_RemovesLeftovers()
        {
            File.WriteAllText(Dll + ModUpdater.PreviousSuffix, "x");
            File.WriteAllText(Dll + ".new", "x");
            ModUpdater.CleanupPrevious(Dll);
            Assert.False(File.Exists(Dll + ModUpdater.PreviousSuffix));
            Assert.False(File.Exists(Dll + ".new"));
            Assert.True(File.Exists(Dll));
        }
    }

    public class UpdateSummaryTests
    {
        private static UpdateResult Result(int added, int updated, int removed, string version = "0.1.3")
        {
            var report = new SyncReport();
            report.Files.Add(new FileSyncResult { File = "a.json", Added = added, Updated = updated, Removed = removed });
            return new UpdateResult { Status = UpdateStatus.Updated, RemoteVersion = version, Report = report };
        }

        [Fact]
        public void AllCounts_AreJoined() =>
            Assert.Equal("Traduções atualizadas para a versão 0.1.3: 121 novas, 4 atualizadas e 2 removidas.",
                UpdateSummary.TranslationsLine(Result(121, 4, 2)));

        [Fact]
        public void ZeroCounts_AreOmitted() =>
            Assert.Equal("Traduções atualizadas para a versão 0.1.3: 12 novas e 3 removidas.",
                UpdateSummary.TranslationsLine(Result(12, 0, 3)));

        [Fact]
        public void Singular_AndThousands() =>
            Assert.Equal("Traduções atualizadas para a versão 0.1.3: 1.234 novas e 1 atualizada.",
                UpdateSummary.TranslationsLine(Result(1234, 1, 0)));

        [Fact]
        public void NothingToCount_GivesNoLine() =>
            Assert.Null(UpdateSummary.TranslationsLine(Result(0, 0, 0)));

        [Fact]
        public void NotUpdated_GivesNoLine() =>
            Assert.Null(UpdateSummary.TranslationsLine(new UpdateResult { Status = UpdateStatus.UpToDate }));

        [Fact]
        public void StagedMod_GivesLine()
        {
            var r = new UpdateResult { ModUpdate = new ModUpdateResult { Status = ModUpdateStatus.Staged, NewVersion = "0.2.0" } };
            Assert.Contains("0.2.0", UpdateSummary.ModStagedLine(r));
            r.ModUpdate.Status = ModUpdateStatus.UpToDate;
            Assert.Null(UpdateSummary.ModStagedLine(r));
        }
    }

    public class ModVersionTrackerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw_modver_" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void FirstRun_ReturnsNull_ThenReportsChange()
        {
            Assert.Null(ModVersionTracker.RecordAndGetPrevious(_dir, "0.1.0"));
            Assert.Null(ModVersionTracker.RecordAndGetPrevious(_dir, "0.1.0"));
            Assert.Equal("0.1.0", ModVersionTracker.RecordAndGetPrevious(_dir, "0.2.0"));
            Assert.Null(ModVersionTracker.RecordAndGetPrevious(_dir, "0.2.0"));
        }
    }
}
