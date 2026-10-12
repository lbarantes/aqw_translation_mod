using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.GitHubPr
{
    public sealed class RepoRef
    {
        public string Owner { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Owner + "/" + Name;
    }

    public sealed class GitHubApiException : Exception
    {
        public int StatusCode { get; }
        public GitHubApiException(int status, string message) : base(message) { StatusCode = status; }
    }

    // As chamadas ao GitHub que o envio de PR precisa — separadas numa interface
    // pra testar o fluxo inteiro com uma implementação falsa.
    public interface IGitHubApi
    {
        Task<string> GetLoginAsync(CancellationToken ct);
        Task<string> GetDefaultBranchAsync(RepoRef repo, CancellationToken ct);
        Task<string> GetBranchHeadAsync(RepoRef repo, string branch, CancellationToken ct);
        Task<string> GetCommitTreeAsync(RepoRef repo, string commitSha, CancellationToken ct);
        /// <summary>Texto do arquivo na branch, ou null se não existir.</summary>
        Task<string?> GetFileTextAsync(RepoRef repo, string path, string reference, CancellationToken ct);
        /// <summary>Repositório onde a branch será criada: o próprio (se for o dono) ou o fork do usuário.</summary>
        Task<RepoRef> EnsureForkAsync(RepoRef upstream, string login, CancellationToken ct);
        Task<string> CreateTreeAsync(RepoRef repo, string baseTree, IReadOnlyDictionary<string, string> files, CancellationToken ct);
        Task<string> CreateCommitAsync(RepoRef repo, string message, string tree, string parent, CancellationToken ct);
        Task CreateBranchAsync(RepoRef repo, string branch, string commitSha, CancellationToken ct);
        Task<string> CreatePullRequestAsync(RepoRef upstream, string title, string head, string baseBranch, string body, CancellationToken ct);
    }

    public sealed class HttpGitHubApi : IGitHubApi
    {
        private const string Api = "https://api.github.com";
        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public HttpGitHubApi(HttpClient http, string token, string userAgent,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _http = http;
            _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<string> GetLoginAsync(CancellationToken ct)
        {
            var j = await SendJsonAsync(HttpMethod.Get, "/user", null, ct).ConfigureAwait(false);
            return (string?)j["login"] ?? throw new GitHubApiException(0, "Resposta sem 'login'.");
        }

        public async Task<string> GetDefaultBranchAsync(RepoRef repo, CancellationToken ct)
        {
            var j = await SendJsonAsync(HttpMethod.Get, RepoPath(repo), null, ct).ConfigureAwait(false);
            return (string?)j["default_branch"] ?? "main";
        }

        public async Task<string> GetBranchHeadAsync(RepoRef repo, string branch, CancellationToken ct)
        {
            var j = await SendJsonAsync(HttpMethod.Get, RepoPath(repo) + "/git/ref/heads/" + Esc(branch, keepSlash: true), null, ct).ConfigureAwait(false);
            return (string?)j["object"]?["sha"] ?? throw new GitHubApiException(0, "Branch sem SHA.");
        }

        public async Task<string> GetCommitTreeAsync(RepoRef repo, string commitSha, CancellationToken ct)
        {
            var j = await SendJsonAsync(HttpMethod.Get, RepoPath(repo) + "/git/commits/" + commitSha, null, ct).ConfigureAwait(false);
            return (string?)j["tree"]?["sha"] ?? throw new GitHubApiException(0, "Commit sem tree.");
        }

        public async Task<string?> GetFileTextAsync(RepoRef repo, string path, string reference, CancellationToken ct)
        {
            var url = Api + RepoPath(repo) + "/contents/" + Esc(path, keepSlash: true) + "?ref=" + Uri.EscapeDataString(reference);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("application/vnd.github.raw+json"); // conteúdo cru, sem limite de 1 MB do base64
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw Error(resp.StatusCode, text);
            return text;
        }

        public async Task<RepoRef> EnsureForkAsync(RepoRef upstream, string login, CancellationToken ct)
        {
            // dono do repositório não precisa de fork: cria a branch nele mesmo
            if (string.Equals(login, upstream.Owner, StringComparison.OrdinalIgnoreCase))
                return upstream;

            var j = await SendJsonAsync(HttpMethod.Post, RepoPath(upstream) + "/forks", new JObject(), ct).ConfigureAwait(false);
            var fork = new RepoRef
            {
                Owner = (string?)j["owner"]?["login"] ?? login,
                Name  = (string?)j["name"] ?? upstream.Name,
            };

            // o fork é criado de forma assíncrona: espera ficar acessível
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    await SendJsonAsync(HttpMethod.Get, RepoPath(fork), null, ct).ConfigureAwait(false);
                    await SendJsonAsync(HttpMethod.Get, RepoPath(fork) + "/git/ref/heads/" + Esc(await GetDefaultBranchAsync(fork, ct).ConfigureAwait(false), true), null, ct).ConfigureAwait(false);
                    return fork;
                }
                catch (GitHubApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 409)
                {
                    await _delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
            }
            throw new GitHubApiException(0, "O fork demorou demais para ficar pronto. Tente de novo em instantes.");
        }

        public async Task<string> CreateTreeAsync(RepoRef repo, string baseTree, IReadOnlyDictionary<string, string> files, CancellationToken ct)
        {
            var tree = new JArray(files.Select(f => new JObject
            {
                ["path"] = f.Key, ["mode"] = "100644", ["type"] = "blob", ["content"] = f.Value,
            }));
            var j = await SendJsonAsync(HttpMethod.Post, RepoPath(repo) + "/git/trees",
                new JObject { ["base_tree"] = baseTree, ["tree"] = tree }, ct).ConfigureAwait(false);
            return (string?)j["sha"] ?? throw new GitHubApiException(0, "Tree sem SHA.");
        }

        public async Task<string> CreateCommitAsync(RepoRef repo, string message, string tree, string parent, CancellationToken ct)
        {
            var j = await SendJsonAsync(HttpMethod.Post, RepoPath(repo) + "/git/commits",
                new JObject { ["message"] = message, ["tree"] = tree, ["parents"] = new JArray(parent) }, ct).ConfigureAwait(false);
            return (string?)j["sha"] ?? throw new GitHubApiException(0, "Commit sem SHA.");
        }

        public async Task CreateBranchAsync(RepoRef repo, string branch, string commitSha, CancellationToken ct)
        {
            await SendJsonAsync(HttpMethod.Post, RepoPath(repo) + "/git/refs",
                new JObject { ["ref"] = "refs/heads/" + branch, ["sha"] = commitSha }, ct).ConfigureAwait(false);
        }

        public async Task<string> CreatePullRequestAsync(RepoRef upstream, string title, string head, string baseBranch, string body, CancellationToken ct)
        {
            var j = await SendJsonAsync(HttpMethod.Post, RepoPath(upstream) + "/pulls", new JObject
            {
                ["title"] = title, ["head"] = head, ["base"] = baseBranch, ["body"] = body,
                ["maintainer_can_modify"] = true,
            }, ct).ConfigureAwait(false);
            return (string?)j["html_url"] ?? throw new GitHubApiException(0, "PR criada sem URL.");
        }

        // ------------------------------------------------------------- interno

        private static string RepoPath(RepoRef r) => $"/repos/{Uri.EscapeDataString(r.Owner)}/{Uri.EscapeDataString(r.Name)}";

        private static string Esc(string s, bool keepSlash) =>
            keepSlash ? string.Join("/", s.Split('/').Select(Uri.EscapeDataString)) : Uri.EscapeDataString(s);

        private async Task<JObject> SendJsonAsync(HttpMethod method, string pathOrUrl, JObject? body, CancellationToken ct)
        {
            var url = pathOrUrl.StartsWith("http", StringComparison.Ordinal) ? pathOrUrl : Api + pathOrUrl;
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (body != null)
                req.Content = new StringContent(body.ToString(Formatting.None), new UTF8Encoding(false), "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw Error(resp.StatusCode, text);

            try { return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text); }
            catch { throw new GitHubApiException((int)resp.StatusCode, "Resposta ilegível do GitHub."); }
        }

        private static GitHubApiException Error(HttpStatusCode status, string text)
        {
            string msg;
            try { msg = (string?)JObject.Parse(text)["message"] ?? text; } catch { msg = text; }
            if (msg.Length > 300) msg = msg.Substring(0, 300) + "…";
            return new GitHubApiException((int)status, $"GitHub respondeu {(int)status}: {msg}");
        }
    }
}
