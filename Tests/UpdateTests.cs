using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    internal static class TestPackage
    {
        public static string Sha(byte[] b)
        {
            using var s = SHA256.Create();
            return string.Concat(s.ComputeHash(b).Select(x => x.ToString("x2")));
        }

        public static string TranslationsJson(params (string k, string v)[] e)
        {
            var t = new JObject();
            foreach (var (k, v) in e) t[k] = v;
            return new JObject { ["translations"] = t }.ToString();
        }

        /// <summary>Gera ZIP + manifest.json consistentes. tamper permite corromper depois de calcular os hashes.</summary>
        public static (string zipPath, string manifestJson) Build(
            string dir, string version, (string path, string content)[] files,
            string? minMod = null, Action<string>? tamperZip = null)
        {
            Directory.CreateDirectory(dir);
            var zipPath = Path.Combine(dir, "translations.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                foreach (var (path, content) in files)
                {
                    var e = zip.CreateEntry(path);
                    using var w = e.Open();
                    var bytes = new UTF8Encoding(false).GetBytes(content);
                    w.Write(bytes, 0, bytes.Length);
                }

            var zipBytes = File.ReadAllBytes(zipPath);
            var manifest = new JObject
            {
                ["schemaVersion"] = 1,
                ["packageVersion"] = version,
                ["package"] = new JObject { ["name"] = "translations.zip", ["sha256"] = Sha(zipBytes), ["sizeBytes"] = zipBytes.Length },
                ["files"] = new JArray(files.Select(f =>
                {
                    var b = new UTF8Encoding(false).GetBytes(f.content);
                    return new JObject { ["path"] = f.path, ["sha256"] = Sha(b), ["sizeBytes"] = b.Length };
                })),
            };
            if (minMod != null) manifest["minModVersion"] = minMod;
            tamperZip?.Invoke(zipPath);
            return (zipPath, manifest.ToString());
        }
    }

    internal sealed class FakeReleaseSource : IReleaseSource
    {
        public string? ManifestJson;
        public string? ZipPath;
        public Exception? ManifestError;
        public int ManifestCalls, DownloadCalls;

        public Task<string> GetManifestAsync(long maxBytes, CancellationToken ct)
        {
            ManifestCalls++;
            if (ManifestError != null) throw ManifestError;
            return Task.FromResult(ManifestJson!);
        }

        /// <summary>Outros assets do release (ex.: DLL do mod), por nome.</summary>
        public Dictionary<string, string> Assets { get; } = new Dictionary<string, string>();

        public Task DownloadPackageAsync(string packageName, string destFile, long maxBytes, CancellationToken ct)
        {
            DownloadCalls++;
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(Assets.TryGetValue(packageName, out var asset) ? asset : ZipPath!, destFile, true);
            return Task.CompletedTask;
        }
    }

    public class PackageVersionTests
    {
        [Theory]
        [InlineData("2026.09.30", "2026.09.29", 1)]
        [InlineData("2026.09.30.2", "2026.09.30", 1)]
        [InlineData("2026.09.30", "2026.09.30.0", 0)]
        [InlineData("2.2.0", "2.10.0", -1)]
        public void Compare(string a, string b, int expected)
        {
            Assert.True(PackageVersion.TryParse(a, out var va));
            Assert.True(PackageVersion.TryParse(b, out var vb));
            Assert.Equal(expected, Math.Sign(va.CompareTo(vb)));
        }

        [Theory]
        [InlineData("")] [InlineData("abc")] [InlineData("1..2")] [InlineData("1.2.3.4.5")] [InlineData("-1.0")] [InlineData("1.2-beta")]
        public void Invalid_IsRejected(string s) => Assert.False(PackageVersion.TryParse(s, out _));
    }

    public class RemoteManifestTests
    {
        private static bool Parse(string json, out RemoteManifest m, out string err) =>
            RemoteManifest.TryParse(json, new SyncLimits(), out m, out err);

        private static string Sha => new string('a', 64);

        private static string Json(string files = "[]", string? extra = null, string pkgName = "translations.zip") =>
            "{\"schemaVersion\":1,\"packageVersion\":\"2026.09.30\"," + (extra ?? "") +
            $"\"package\":{{\"name\":\"{pkgName}\",\"sha256\":\"{Sha}\",\"sizeBytes\":100}},\"files\":{files}}}";

        [Fact]
        public void Valid_Parses() =>
            Assert.True(Parse(Json($"[{{\"path\":\"ui/hud.json\",\"sha256\":\"{Sha}\",\"sizeBytes\":10}}]"), out var m, out _) && m.Files.Count == 1);

        [Theory]
        [InlineData("../evil.json")] [InlineData("/abs.json")] [InlineData("C:/x.json")] [InlineData("overrides/a.json")]
        [InlineData("_capture/a.json")] [InlineData("uncategorized.json")] [InlineData("a\\b.json")] [InlineData("a.txt")]
        public void BadPaths_AreRejected(string path) =>
            Assert.False(Parse(Json($"[{{\"path\":\"{path.Replace("\\", "\\\\")}\",\"sha256\":\"{Sha}\",\"sizeBytes\":10}}]"), out _, out _));

        [Fact] public void WrongSchema_IsRejected() =>
            Assert.False(Parse(Json().Replace("\"schemaVersion\":1", "\"schemaVersion\":2"), out _, out _));

        [Fact] public void BadPackageName_IsRejected() =>
            Assert.False(Parse(Json(pkgName: "../x.zip"), out _, out _));

        [Fact] public void BadHash_IsRejected() =>
            Assert.False(Parse(Json("[{\"path\":\"a.json\",\"sha256\":\"zz\",\"sizeBytes\":1}]"), out _, out _));

        [Fact] public void DuplicatePaths_AreRejected() =>
            Assert.False(Parse(Json($"[{{\"path\":\"a.json\",\"sha256\":\"{Sha}\",\"sizeBytes\":1}},{{\"path\":\"A.json\",\"sha256\":\"{Sha}\",\"sizeBytes\":1}}]"), out _, out _));

        [Fact] public void Garbage_IsRejected() => Assert.False(Parse("not json", out _, out _));
    }

    public class HttpReleaseSourceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw_http_" + Guid.NewGuid().ToString("N"));
        public HttpReleaseSourceTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private sealed class StubHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
            public List<Uri> Requests = new List<Uri>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add(request.RequestUri!);
                var r = Respond(request); r.RequestMessage = request;
                return Task.FromResult(r);
            }
        }

        private static HttpResponseMessage Bytes(int n) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[n]) };

        [Fact]
        public async Task Manifest_UsesStableLatestDownloadUrl()
        {
            var h = new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"x\":1}") } };
            using var src = new HttpReleaseSource("lbarantes/aqw_translation_mod", "t/1", h);
            var text = await src.GetManifestAsync(1000, CancellationToken.None);

            Assert.Equal("{\"x\":1}", text);
            Assert.Equal("https://github.com/lbarantes/aqw_translation_mod/releases/latest/download/manifest.json", h.Requests.Single().ToString());
        }

        [Fact]
        public async Task NotFound_BecomesReleaseNotFound()
        {
            var h = new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
            using var src = new HttpReleaseSource("a/b", "t/1", h);
            await Assert.ThrowsAsync<ReleaseNotFoundException>(() => src.GetManifestAsync(1000, CancellationToken.None));
        }

        [Fact]
        public async Task ServerError_ThrowsHttpRequestException()
        {
            var h = new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
            using var src = new HttpReleaseSource("a/b", "t/1", h);
            await Assert.ThrowsAsync<HttpRequestException>(() => src.GetManifestAsync(1000, CancellationToken.None));
        }

        [Fact]
        public async Task OversizedDownload_IsAborted_AndPartialFileRemoved()
        {
            var h = new StubHandler { Respond = _ => Bytes(5000) };
            using var src = new HttpReleaseSource("a/b", "t/1", h);
            var dest = Path.Combine(_dir, "p.zip");
            await Assert.ThrowsAsync<InvalidDataException>(() => src.DownloadPackageAsync("translations.zip", dest, 1000, CancellationToken.None));
            Assert.False(File.Exists(dest));
        }

        [Fact]
        public async Task NonHttpsFinalUri_IsRefused()
        {
            var h = new StubHandler { Respond = req =>
            {
                var r = Bytes(10);
                r.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://evil.example/x");
                return r;
            } };
            // StubHandler sobrescreve RequestMessage; usa um handler que não sobrescreve
            var h2 = new FixedUriHandler();
            using var src = new HttpReleaseSource("a/b", "t/1", h2);
            await Assert.ThrowsAsync<HttpRequestException>(() => src.GetManifestAsync(1000, CancellationToken.None));
        }

        private sealed class FixedUriHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}"),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://evil.example/x"),
                });
        }

        [Theory]
        [InlineData("noslash")] [InlineData("a/b/c")] [InlineData("a/b c")] [InlineData("../x")]
        public void BadRepository_IsRejected(string repo) =>
            Assert.Throws<ArgumentException>(() => new HttpReleaseSource(repo, "t/1"));
    }

    public class UpdateCoordinatorTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw_upd_" + Guid.NewGuid().ToString("N"));
        private string Local => Path.Combine(_root, "local");
        private string Pkg   => Path.Combine(_root, "pkg");

        public UpdateCoordinatorTests() { Directory.CreateDirectory(Local); Directory.CreateDirectory(Pkg); }
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        private Task<UpdateResult> Run(FakeReleaseSource src, string modVersion = "2.2.0") =>
            UpdateCoordinator.RunAsync(new UpdateOptions { LocalRoot = Local, ModVersion = modVersion }, src, CancellationToken.None);

        private FakeReleaseSource Release(string version, string? minMod = null, Action<string>? tamper = null,
            params (string k, string v)[] hud)
        {
            var (zip, manifest) = TestPackage.Build(Pkg, version,
                new[] { ("ui/hud.json", TestPackage.TranslationsJson(hud.Length == 0 ? new[] { ("Play", "Jogar") } : hud)) },
                minMod, tamper);
            return new FakeReleaseSource { ManifestJson = manifest, ZipPath = zip };
        }

        private JObject Hud() => (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Local, "ui", "hud.json")))["translations"]!;

        [Fact]
        public async Task FirstUpdate_InstallsPackage_AndCleansStaging()
        {
            var r = await Run(Release("2026.09.30"));

            Assert.Equal(UpdateStatus.Updated, r.Status);
            Assert.Equal("Jogar", (string)Hud()["Play"]!);
            Assert.False(Directory.Exists(Path.Combine(Local, "_sync", "staging")));
            Assert.Contains("2026.09.30", File.ReadAllText(Path.Combine(Local, "_sync", "state.json")));
        }

        [Fact]
        public async Task SameVersion_IsUpToDate_WithoutDownloading()
        {
            var src = Release("2026.09.30");
            await Run(src);
            src.DownloadCalls = 0;

            var r = await Run(src);
            Assert.Equal(UpdateStatus.UpToDate, r.Status);
            Assert.Equal(0, src.DownloadCalls);
        }

        [Fact]
        public async Task OlderRemote_IsIgnored_NoDowngrade()
        {
            await Run(Release("2026.10.01"));
            var src = Release("2026.09.01", hud: new[] { ("Play", "Velho") });
            var r = await Run(src);

            Assert.Equal(UpdateStatus.UpToDate, r.Status);
            Assert.Equal("Jogar", (string)Hud()["Play"]!);
        }

        [Fact]
        public async Task NewerRemote_UpdatesUntouchedEntries_AndKeepsLocalEdits()
        {
            await Run(Release("2026.09.30", hud: new[] { ("Play", "Jogar"), ("Quit", "Sair") }));

            var hud = Path.Combine(Local, "ui", "hud.json");
            File.WriteAllText(hud, TestPackage.TranslationsJson(("Play", "Começar"), ("Quit", "Sair")));

            var r = await Run(Release("2026.10.05", hud: new[] { ("Play", "Jogar"), ("Quit", "Sair do jogo") }));
            Assert.Equal(UpdateStatus.Updated, r.Status);
            Assert.Equal("Começar", (string)Hud()["Play"]!);
            Assert.Equal("Sair do jogo", (string)Hud()["Quit"]!);
            Assert.Empty(r.Report!.Conflicts);
        }

        [Fact]
        public async Task Update_BacksUpOverwrittenFiles()
        {
            await Run(Release("2026.09.30"));
            var r = await Run(Release("2026.10.05", hud: new[] { ("Play", "Iniciar") }));

            var backups = Directory.GetDirectories(Path.Combine(Local, "_sync", "backup"));
            Assert.Single(backups);
            Assert.True(File.Exists(Path.Combine(backups[0], "ui", "hud.json")));
        }

        [Fact]
        public async Task TooNewForThisMod_IsIncompatible_AndNothingChanges()
        {
            var r = await Run(Release("2026.09.30", minMod: "9.0.0"), modVersion: "2.2.0");
            Assert.Equal(UpdateStatus.Incompatible, r.Status);
            Assert.False(File.Exists(Path.Combine(Local, "ui", "hud.json")));
        }

        [Fact]
        public async Task TamperedZip_IsRejected_AndNothingInstalled()
        {
            var src = Release("2026.09.30", tamper: zip =>
            {
                var bytes = File.ReadAllBytes(zip);
                bytes[bytes.Length / 2] ^= 0xFF;
                File.WriteAllBytes(zip, bytes);
            });
            var r = await Run(src);

            Assert.Equal(UpdateStatus.Failed, r.Status);
            Assert.False(File.Exists(Path.Combine(Local, "ui", "hud.json")));
            Assert.False(Directory.Exists(Path.Combine(Local, "_sync", "staging")));
        }

        [Fact]
        public async Task ZipWithExtraFileNotInManifest_IsRejected()
        {
            var (zip, manifest) = TestPackage.Build(Pkg, "2026.09.30",
                new[] { ("ui/hud.json", TestPackage.TranslationsJson(("A", "a"))), ("sneaky.json", "{}") });
            var m = JObject.Parse(manifest);
            ((JArray)m["files"]!).RemoveAt(1);
            // recalcula o hash do zip pra isolar o teste: o manifesto é "válido" mas não lista o extra
            var zb = File.ReadAllBytes(zip);
            m["package"]!["sha256"] = TestPackage.Sha(zb);

            var r = await Run(new FakeReleaseSource { ManifestJson = m.ToString(), ZipPath = zip });
            Assert.Equal(UpdateStatus.Failed, r.Status);
            Assert.Contains("manifesto", r.Message);
            Assert.False(File.Exists(Path.Combine(Local, "sneaky.json")));
        }

        [Fact]
        public async Task ZipMissingAFileListedInManifest_IsRejected()
        {
            var (zip, manifest) = TestPackage.Build(Pkg, "2026.09.30",
                new[] { ("ui/hud.json", TestPackage.TranslationsJson(("A", "a"))), ("ui/extra.json", "{}") });
            var m = JObject.Parse(manifest);
            // reconstrói o ZIP sem o segundo arquivo, mantendo o manifesto que ainda o lista
            var (zip2, _) = TestPackage.Build(Path.Combine(_root, "pkg2"), "2026.09.30",
                new[] { ("ui/hud.json", TestPackage.TranslationsJson(("A", "a"))) });
            var zb = File.ReadAllBytes(zip2);
            m["package"]!["sha256"] = TestPackage.Sha(zb);
            m["package"]!["sizeBytes"] = zb.Length;

            var r = await Run(new FakeReleaseSource { ManifestJson = m.ToString(), ZipPath = zip2 });
            Assert.Equal(UpdateStatus.Failed, r.Status);
            Assert.Contains("incompleto", r.Message);
        }

        [Fact]
        public async Task NoRelease_IsReported_NotAnError()
        {
            var r = await Run(new FakeReleaseSource { ManifestError = new ReleaseNotFoundException("sem release") });
            Assert.Equal(UpdateStatus.NoRelease, r.Status);
        }

        [Fact]
        public async Task NetworkFailure_IsOffline()
        {
            var r = await Run(new FakeReleaseSource { ManifestError = new HttpRequestException("dns") });
            Assert.Equal(UpdateStatus.Offline, r.Status);
        }

        [Fact]
        public async Task Offline_LeavesLocalFilesUntouched()
        {
            Directory.CreateDirectory(Path.Combine(Local, "ui"));
            var hud = Path.Combine(Local, "ui", "hud.json");
            File.WriteAllText(hud, "meu conteudo");
            await Run(new FakeReleaseSource { ManifestError = new HttpRequestException("x") });
            Assert.Equal("meu conteudo", File.ReadAllText(hud));
        }

        [Fact]
        public async Task OldBackups_ArePruned()
        {
            var b = Path.Combine(Local, "_sync", "backup");
            foreach (var n in new[] { "20200101_000001", "20200101_000002", "20200101_000003", "20200101_000004" })
                Directory.CreateDirectory(Path.Combine(b, n));

            await Run(Release("2026.09.30"));
            var r = await Run(Release("2026.10.05", hud: new[] { ("Play", "Iniciar") }));

            Assert.True(Directory.GetDirectories(b).Length <= 3);
        }

        [Fact]
        public async Task ManifestOfGarbage_IsFailed()
        {
            var r = await Run(new FakeReleaseSource { ManifestJson = "<html>oops</html>" });
            Assert.Equal(UpdateStatus.Failed, r.Status);
        }
    }
}
