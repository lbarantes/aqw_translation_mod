using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.GitHubPr;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string Body)> Calls = new List<(HttpMethod, string, string)>();
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();

        public ScriptedHandler Json(object body, HttpStatusCode code = HttpStatusCode.OK)
        {
            _responses.Enqueue(_ => new HttpResponseMessage(code) { Content = new StringContent(JToken.FromObject(body).ToString(), Encoding.UTF8, "application/json") });
            return this;
        }

        public ScriptedHandler Raw(string text, HttpStatusCode code = HttpStatusCode.OK)
        {
            _responses.Enqueue(_ => new HttpResponseMessage(code) { Content = new StringContent(text) });
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content != null ? await req.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
            Calls.Add((req.Method, req.RequestUri!.AbsoluteUri, body));
            if (_responses.Count == 0) throw new InvalidOperationException("sem resposta roteirizada para " + req.RequestUri);
            return _responses.Dequeue()(req);
        }
    }

    public class DeviceFlowTests
    {
        private static DeviceFlowClient Client(ScriptedHandler h, List<TimeSpan>? delays = null) =>
            new DeviceFlowClient(new HttpClient(h), "client123", "public_repo",
                (t, ct) => { delays?.Add(t); return Task.CompletedTask; });

        private static object CodeResp(string uri = "https://github.com/login/device") => new
        {
            device_code = "DEV", user_code = "ABCD-1234", verification_uri = uri, expires_in = 900, interval = 5,
        };

        [Fact]
        public async Task RequestCode_SendsClientIdAndScope_AndParsesResponse()
        {
            var h = new ScriptedHandler().Json(CodeResp());
            var info = await Client(h).RequestCodeAsync(CancellationToken.None);

            Assert.Equal("ABCD-1234", info.UserCode);
            Assert.Equal("DEV", info.DeviceCode);
            Assert.Equal("https://github.com/login/device/code", h.Calls[0].Url);
            Assert.Contains("client_id=client123", h.Calls[0].Body);
            Assert.Contains("scope=public_repo", h.Calls[0].Body);
        }

        [Theory]
        [InlineData("http://github.com/login/device")]
        [InlineData("https://evil.example/login/device")]
        [InlineData("https://github.com.evil.example/login/device")]
        public async Task RequestCode_RefusesNonGitHubVerificationUri(string uri)
        {
            var h = new ScriptedHandler().Json(CodeResp(uri));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Client(h).RequestCodeAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Poll_WaitsWhilePending_ThenReturnsToken()
        {
            var h = new ScriptedHandler()
                .Json(new { error = "authorization_pending" })
                .Json(new { error = "authorization_pending" })
                .Json(new { access_token = "gho_secret", token_type = "bearer" });
            var delays = new List<TimeSpan>();

            var r = await Client(h, delays).PollAsync(new DeviceCodeInfo { DeviceCode = "DEV", ExpiresInSeconds = 900, IntervalSeconds = 5 }, CancellationToken.None);

            Assert.Equal(DeviceFlowStatus.Authorized, r.Status);
            Assert.Equal("gho_secret", r.AccessToken);
            Assert.Equal(3, h.Calls.Count);
            Assert.All(delays, d => Assert.Equal(TimeSpan.FromSeconds(5), d));
            Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", h.Calls[0].Body);
        }

        [Fact]
        public async Task Poll_SlowDown_IncreasesInterval()
        {
            var h = new ScriptedHandler()
                .Json(new { error = "slow_down", interval = 10 })
                .Json(new { access_token = "t" });
            var delays = new List<TimeSpan>();

            await Client(h, delays).PollAsync(new DeviceCodeInfo { DeviceCode = "D", ExpiresInSeconds = 900, IntervalSeconds = 5 }, CancellationToken.None);
            Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }, delays);
        }

        [Fact]
        public async Task Poll_SlowDownWithoutInterval_AddsFiveSeconds()
        {
            var h = new ScriptedHandler().Json(new { error = "slow_down" }).Json(new { access_token = "t" });
            var delays = new List<TimeSpan>();
            await Client(h, delays).PollAsync(new DeviceCodeInfo { DeviceCode = "D", ExpiresInSeconds = 900, IntervalSeconds = 5 }, CancellationToken.None);
            Assert.Equal(TimeSpan.FromSeconds(10), delays[1]);
        }

        [Theory]
        [InlineData("access_denied", DeviceFlowStatus.Denied)]
        [InlineData("expired_token", DeviceFlowStatus.Expired)]
        [InlineData("incorrect_device_code", DeviceFlowStatus.Failed)]
        public async Task Poll_TerminalErrors_AreMapped(string error, DeviceFlowStatus expected)
        {
            var h = new ScriptedHandler().Json(new { error });
            var r = await Client(h).PollAsync(new DeviceCodeInfo { DeviceCode = "D", ExpiresInSeconds = 900, IntervalSeconds = 1 }, CancellationToken.None);
            Assert.Equal(expected, r.Status);
            Assert.Null(r.AccessToken);
        }

        [Fact]
        public async Task Poll_Cancellation_Propagates()
        {
            var cts = new CancellationTokenSource();
            var client = new DeviceFlowClient(new HttpClient(new ScriptedHandler().Json(new { error = "authorization_pending" })),
                "c", "s", (t, ct) => { cts.Cancel(); return Task.CompletedTask; });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.PollAsync(new DeviceCodeInfo { DeviceCode = "D", ExpiresInSeconds = 900, IntervalSeconds = 1 }, cts.Token));
        }
    }

    public class TokenStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw_tok_" + Guid.NewGuid().ToString("N"));
        public TokenStoreTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private sealed class XorProtector : ISecretProtector
        {
            public byte[] Protect(byte[] p) => p.Select(b => (byte)(b ^ 0x5A)).ToArray();
            public byte[] Unprotect(byte[] p) => Protect(p);
        }

        private sealed class BrokenProtector : ISecretProtector
        {
            public byte[] Protect(byte[] p) => throw new InvalidOperationException("sem DPAPI");
            public byte[] Unprotect(byte[] p) => throw new InvalidOperationException("sem DPAPI");
        }

        [Fact]
        public void SaveLoad_RoundTrips_AndFileDoesNotContainPlainToken()
        {
            var path = Path.Combine(_dir, "t.token");
            var store = new TokenStore(path, new XorProtector());
            Assert.True(store.Save("gho_supersecret"));

            Assert.DoesNotContain("gho_supersecret", File.ReadAllText(path));
            Assert.Equal("gho_supersecret", new TokenStore(path, new XorProtector()).Load());
        }

        [Fact]
        public void Load_WithoutFile_ReturnsNull() =>
            Assert.Null(new TokenStore(Path.Combine(_dir, "none.token"), new XorProtector()).Load());

        [Fact]
        public void Save_WhenProtectionFails_DoesNotWritePlainText()
        {
            var path = Path.Combine(_dir, "t.token");
            Assert.False(new TokenStore(path, new BrokenProtector()).Save("gho_secret"));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Load_CorruptedFile_ReturnsNull()
        {
            var path = Path.Combine(_dir, "t.token");
            File.WriteAllText(path, "%%% not base64 %%%");
            Assert.Null(new TokenStore(path, new XorProtector()).Load());
        }

        [Fact]
        public void Clear_RemovesFile()
        {
            var path = Path.Combine(_dir, "t.token");
            var s = new TokenStore(path, new XorProtector());
            s.Save("x"); s.Clear();
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void RealDpapi_RoundTrips_OnThisMachine()
        {
            var path = Path.Combine(_dir, "dpapi.token");
            var s = new TokenStore(path, new DpapiSecretProtector());
            Assert.True(s.Save("gho_real"));
            Assert.Equal("gho_real", s.Load());
        }
    }

    public class PullRequestPatcherTests
    {
        private static FileChange Change(params EntryChange[] c)
        {
            var f = new FileChange { File = "ui/hud.json" };
            f.Changes.AddRange(c);
            return f;
        }

        private static EntryChange Add(string k, string v) => new EntryChange { Key = k, Kind = ChangeKind.Added, After = new JValue(v) };
        private static EntryChange Mod(string k, string b, string a) => new EntryChange { Key = k, Kind = ChangeKind.Modified, Before = new JValue(b), After = new JValue(a) };

        private static string Up(params (string k, string v)[] e) => TestPackage.TranslationsJson(e);
        private static JObject Entries(string text) => (JObject)JObject.Parse(text)["translations"]!;

        [Fact]
        public void KeepsOthersWork_AndAppliesOnlyMyEntries()
        {
            var warnings = new List<string>();
            var text = PullRequestPatcher.Apply(Up(("Play", "Jogar"), ("Theirs", "Deles")), Change(Add("Mine", "Minha")), warnings);

            var e = Entries(text!);
            Assert.Equal("Deles", (string)e["Theirs"]!);
            Assert.Equal("Minha", (string)e["Mine"]!);
            Assert.Empty(warnings);
        }

        [Fact]
        public void AlreadyPresentUpstream_IsNoOp() =>
            Assert.Null(PullRequestPatcher.Apply(Up(("Play", "Jogar")), Change(Add("Play", "Jogar")), new List<string>()));

        [Fact]
        public void UpstreamChangedSinceMyBase_WarnsAndReplaces()
        {
            var warnings = new List<string>();
            var text = PullRequestPatcher.Apply(Up(("Play", "Iniciar")), Change(Mod("Play", "Jogar", "Começar")), warnings);

            Assert.Equal("Começar", (string)Entries(text!)["Play"]!);
            Assert.Contains(warnings, w => w.Contains("Play"));
        }

        [Fact]
        public void NewFile_UploadsLocalFileAsIs()
        {
            var f = new FileChange { File = "classes/mine.json", IsNewFile = true, LocalRoot = JObject.Parse(Up(("A", "a"))) };
            f.Changes.Add(Add("A", "a"));

            var text = PullRequestPatcher.Apply(null, f, new List<string>());
            Assert.Equal("a", (string)Entries(text!)["A"]!);
            Assert.EndsWith("\n", text);
        }

        [Fact]
        public void Removal_DeletesKeyUpstream()
        {
            var f = Change(new EntryChange { Key = "Old", Kind = ChangeKind.Removed, Before = new JValue("x") });
            var text = PullRequestPatcher.Apply(Up(("Old", "x"), ("Keep", "k")), f, new List<string>());
            Assert.False(Entries(text!).ContainsKey("Old"));
            Assert.True(Entries(text!).ContainsKey("Keep"));
        }

        [Fact]
        public void PreservesUpstreamMeta()
        {
            var up = "{\"$meta\":{\"locale\":\"pt-BR\",\"version\":\"9\"},\"translations\":{\"A\":\"a\"}}";
            var text = PullRequestPatcher.Apply(up, Change(Add("B", "b")), new List<string>());
            Assert.Equal("9", (string)JObject.Parse(text!)["$meta"]!["version"]!);
        }

        [Fact]
        public void LegacyFlatFile_StaysFlat()
        {
            var text = PullRequestPatcher.Apply("{\"A\":\"a\"}", Change(Add("B", "b")), new List<string>());
            var root = JObject.Parse(text!);
            Assert.Equal("b", (string)root["B"]!);
            Assert.Null(root["translations"]);
        }
    }

    internal sealed class FakeGitHubApi : IGitHubApi
    {
        public string Login = "tradutora";
        public Dictionary<string, string> UpstreamFiles = new Dictionary<string, string>();
        public RepoRef? Fork;
        public List<string> Log = new List<string>();
        public IReadOnlyDictionary<string, string>? TreeFiles;
        public string? CommitMessage, Branch, PrHead, PrTitle, PrBody, PrBase;
        public Exception? FailAt;
        public string? FailStep;

        private void Step(string name) { Log.Add(name); if (FailStep == name && FailAt != null) throw FailAt; }

        public Task<string> GetLoginAsync(CancellationToken ct) { Step("login"); return Task.FromResult(Login); }
        public Task<string> GetDefaultBranchAsync(RepoRef r, CancellationToken ct) { Step("defaultBranch"); return Task.FromResult("main"); }
        public Task<string> GetBranchHeadAsync(RepoRef r, string b, CancellationToken ct) { Step("head"); return Task.FromResult("basecommit"); }
        public Task<string> GetCommitTreeAsync(RepoRef r, string c, CancellationToken ct) { Step("commitTree"); return Task.FromResult("basetree"); }
        public Task<string?> GetFileTextAsync(RepoRef r, string path, string reference, CancellationToken ct)
        {
            Step("file:" + path);
            return Task.FromResult(UpstreamFiles.TryGetValue(path, out var t) ? t : null);
        }
        public Task<RepoRef> EnsureForkAsync(RepoRef up, string login, CancellationToken ct)
        {
            Step("fork");
            return Task.FromResult(string.Equals(login, up.Owner, StringComparison.OrdinalIgnoreCase)
                ? up : (Fork ??= new RepoRef { Owner = login, Name = up.Name }));
        }
        public Task<string> CreateTreeAsync(RepoRef r, string baseTree, IReadOnlyDictionary<string, string> files, CancellationToken ct)
        { Step("tree"); TreeFiles = files; return Task.FromResult("newtree"); }
        public Task<string> CreateCommitAsync(RepoRef r, string m, string tree, string parent, CancellationToken ct)
        { Step("commit"); CommitMessage = m; return Task.FromResult("newcommit"); }
        public Task CreateBranchAsync(RepoRef r, string branch, string sha, CancellationToken ct)
        { Step("branch"); Branch = branch; return Task.CompletedTask; }
        public Task<string> CreatePullRequestAsync(RepoRef up, string title, string head, string baseBranch, string body, CancellationToken ct)
        { Step("pr"); PrTitle = title; PrHead = head; PrBase = baseBranch; PrBody = body; return Task.FromResult("https://github.com/o/r/pull/7"); }
    }

    public class PullRequestSubmitterTests
    {
        private static readonly RepoRef Upstream = new RepoRef { Owner = "lbarantes", Name = "aqw_translation_mod" };
        private static readonly DateTime Now = new DateTime(2026, 10, 12, 14, 30, 5, DateTimeKind.Utc);

        private static ChangeSet Changes(string file = "ui/hud.json", string key = "Play", string val = "Jogar")
        {
            var cs = new ChangeSet();
            var f = new FileChange { File = file };
            f.Changes.Add(new EntryChange { Key = key, Kind = ChangeKind.Added, After = new JValue(val) });
            cs.Files.Add(f);
            return cs;
        }

        private static PullRequestContent Content(ChangeSet cs) =>
            PullRequestComposer.Compose(cs, new ComposeOptions { Locale = "pt-BR" });

        [Fact]
        public async Task HappyPath_AsContributor_ForksAndOpensCrossRepoPr()
        {
            var api = new FakeGitHubApi { Login = "tradutora" };
            api.UpstreamFiles["translations/ui/hud.json"] = TestPackage.TranslationsJson(("Quit", "Sair"));
            var cs = Changes();

            var r = await PullRequestSubmitter.SubmitAsync(api, Upstream, cs, Content(cs), Now, CancellationToken.None);

            Assert.True(r.Success, r.Message);
            Assert.Equal("https://github.com/o/r/pull/7", r.PullRequestUrl);
            Assert.Equal("translations/tradutora/20261012-143005", api.Branch);
            Assert.Equal("tradutora:translations/tradutora/20261012-143005", api.PrHead);
            Assert.Equal("main", api.PrBase);
            Assert.StartsWith("[pt-BR]", api.PrTitle);

            var uploaded = JObject.Parse(api.TreeFiles!["translations/ui/hud.json"]);
            Assert.Equal("Jogar", (string)uploaded["translations"]!["Play"]!);
            Assert.Equal("Sair", (string)uploaded["translations"]!["Quit"]!);   // trabalho dos outros preservado
        }

        [Fact]
        public async Task HappyPath_AsOwner_UsesBranchInSameRepo()
        {
            var api = new FakeGitHubApi { Login = "lbarantes" };
            var cs = Changes();
            var r = await PullRequestSubmitter.SubmitAsync(api, Upstream, cs, Content(cs), Now, CancellationToken.None);

            Assert.True(r.Success);
            Assert.Equal("translations/lbarantes/20261012-143005", api.PrHead); // sem "owner:"
        }

        [Fact]
        public async Task NothingNew_StopsBeforeTouchingGitHubState()
        {
            var api = new FakeGitHubApi();
            api.UpstreamFiles["translations/ui/hud.json"] = TestPackage.TranslationsJson(("Play", "Jogar"));
            var cs = Changes();

            var r = await PullRequestSubmitter.SubmitAsync(api, Upstream, cs, Content(cs), Now, CancellationToken.None);

            Assert.False(r.Success);
            Assert.DoesNotContain("fork", api.Log);
            Assert.DoesNotContain("branch", api.Log);
            Assert.DoesNotContain("pr", api.Log);
        }

        [Fact]
        public async Task EmptyChangeSet_IsRefused_WithoutAnyCall()
        {
            var api = new FakeGitHubApi();
            var r = await PullRequestSubmitter.SubmitAsync(api, Upstream, new ChangeSet(), new PullRequestContent(), Now, CancellationToken.None);
            Assert.False(r.Success);
            Assert.Empty(api.Log);
        }

        [Fact]
        public async Task ApiFailure_IsReported_AndStopsTheFlow()
        {
            var api = new FakeGitHubApi { FailStep = "tree", FailAt = new GitHubApiException(422, "GitHub respondeu 422: bad tree") };
            var cs = Changes();
            var r = await PullRequestSubmitter.SubmitAsync(api, Upstream, cs, Content(cs), Now, CancellationToken.None);

            Assert.False(r.Success);
            Assert.Contains("422", r.Message);
            Assert.DoesNotContain("pr", api.Log);
        }

        [Fact]
        public async Task TooManyFiles_IsRefused()
        {
            var cs = new ChangeSet();
            for (int i = 0; i < PullRequestSubmitter.MaxFiles + 1; i++)
            {
                var f = new FileChange { File = $"a/f{i}.json" };
                f.Changes.Add(new EntryChange { Key = "k", Kind = ChangeKind.Added, After = new JValue("v") });
                cs.Files.Add(f);
            }
            var r = await PullRequestSubmitter.SubmitAsync(new FakeGitHubApi(), Upstream, cs, Content(cs), Now, CancellationToken.None);
            Assert.False(r.Success);
        }

        [Fact]
        public void BranchName_StripsUnsafeCharacters() =>
            Assert.Equal("translations/ab/20261012-143005", PullRequestSubmitter.BranchName("a/../b", Now));
    }

    public class HttpGitHubApiTests
    {
        private static HttpGitHubApi Api(ScriptedHandler h) =>
            new HttpGitHubApi(new HttpClient(h), "tok", "AQW/1", (t, ct) => Task.CompletedTask);

        private static readonly RepoRef Repo = new RepoRef { Owner = "lbarantes", Name = "aqw_translation_mod" };

        [Fact]
        public async Task AuthenticatedRequests_CarryBearerTokenAndApiVersion()
        {
            HttpRequestMessage? seen = null;
            var h = new ScriptedHandler();
            h.Json(new { login = "x" });
            var client = new HttpClient(new CapturingHandler(h, r => seen = r));
            var api = new HttpGitHubApi(client, "tok_abc", "AQW/1");

            await api.GetLoginAsync(CancellationToken.None);

            Assert.Equal("Bearer", seen!.Headers.Authorization!.Scheme);
            Assert.Equal("tok_abc", seen.Headers.Authorization.Parameter);
            Assert.Contains("2022-11-28", seen.Headers.GetValues("X-GitHub-Api-Version"));
        }

        private sealed class CapturingHandler : DelegatingHandler
        {
            private readonly Action<HttpRequestMessage> _on;
            public CapturingHandler(HttpMessageHandler inner, Action<HttpRequestMessage> on) : base(inner) { _on = on; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { _on(r); return base.SendAsync(r, ct); }
        }

        [Fact]
        public async Task GetFile_NotFound_ReturnsNull()
        {
            var h = new ScriptedHandler().Raw("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound);
            Assert.Null(await Api(h).GetFileTextAsync(Repo, "translations/a b/x.json", "main", CancellationToken.None));
            Assert.Contains("translations/a%20b/x.json", h.Calls[0].Url);   // cada segmento escapado, '/' mantido
        }

        [Fact]
        public async Task CreateTree_SendsInlineContentAndBaseTree()
        {
            var h = new ScriptedHandler().Json(new { sha = "T" });
            var sha = await Api(h).CreateTreeAsync(Repo, "BASE",
                new Dictionary<string, string> { ["translations/ui/hud.json"] = "{}\n" }, CancellationToken.None);

            Assert.Equal("T", sha);
            var body = JObject.Parse(h.Calls[0].Body);
            Assert.Equal("BASE", (string)body["base_tree"]!);
            Assert.Equal("{}\n", (string)body["tree"]![0]!["content"]!);
            Assert.Equal("100644", (string)body["tree"]![0]!["mode"]!);
        }

        [Fact]
        public async Task Error_SurfacesGitHubMessage()
        {
            var h = new ScriptedHandler().Raw("{\"message\":\"Bad credentials\"}", HttpStatusCode.Unauthorized);
            var ex = await Assert.ThrowsAsync<GitHubApiException>(() => Api(h).GetLoginAsync(CancellationToken.None));
            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("Bad credentials", ex.Message);
        }

        [Fact]
        public async Task Owner_DoesNotFork()
        {
            var h = new ScriptedHandler();
            var r = await Api(h).EnsureForkAsync(Repo, "lbarantes", CancellationToken.None);
            Assert.Equal("lbarantes", r.Owner);
            Assert.Empty(h.Calls);
        }

        [Fact]
        public async Task Contributor_CreatesFork_AndWaitsUntilReachable()
        {
            var h = new ScriptedHandler()
                .Json(new { name = "aqw_translation_mod", owner = new { login = "tradutora" } }, HttpStatusCode.Accepted) // POST forks
                .Raw("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound)                                              // fork ainda não existe
                .Json(new { default_branch = "main" })                                                                     // GET repo
                .Json(new { default_branch = "main" })                                                                     // default_branch (GetDefaultBranchAsync)
                .Json(new { @object = new { sha = "abc" } });                                                              // GET ref

            var fork = await Api(h).EnsureForkAsync(Repo, "tradutora", CancellationToken.None);

            Assert.Equal("tradutora", fork.Owner);
            Assert.Equal(HttpMethod.Post, h.Calls[0].Method);
            Assert.EndsWith("/forks", h.Calls[0].Url);
        }
    }
}
