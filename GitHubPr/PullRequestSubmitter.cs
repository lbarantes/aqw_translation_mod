using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Sync;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.GitHubPr
{
    // Aplica as mudanças locais (FileChange) por cima do arquivo ATUAL do
    // repositório oficial — não sobe o arquivo local inteiro. Assim a PR só
    // carrega as entradas do usuário e nunca desfaz o trabalho de outros.
    public static class PullRequestPatcher
    {
        /// <summary>
        /// Texto novo do arquivo, ou null se não há nada a mudar (tudo já igual no GitHub).
        /// </summary>
        public static string? Apply(string? upstreamText, FileChange change, List<string> warnings)
        {
            // arquivo que o GitHub não tem: sobe o arquivo local como está
            if (upstreamText == null)
            {
                var template = change.LocalRoot ?? new JObject { ["translations"] = new JObject() };
                var fresh = change.LocalRoot == null
                    ? SyncEngine.WithNode(template, ToNode(change))
                    : template;
                return Serialize(fresh);
            }

            var root = JObject.Parse(upstreamText);
            var node = SyncEngine.Node(root);
            bool changed = false;

            foreach (var c in change.Changes)
            {
                var current = node[c.Key];
                switch (c.Kind)
                {
                    case ChangeKind.Added:
                    case ChangeKind.Modified:
                        if (EntryCanon.Same(current, c.After)) break;
                        if (current != null && !EntryCanon.Same(current, c.Before))
                            warnings.Add($"'{change.File}' → '{c.Key}': o GitHub já tem outro valor, que seria substituído pelo seu.");
                        node[c.Key] = c.After!.DeepClone();
                        changed = true;
                        break;

                    case ChangeKind.Removed:
                        if (current != null) { node.Remove(c.Key); changed = true; }
                        break;
                }
            }

            return changed ? Serialize(SyncEngine.WithNode(root, node)) : null;
        }

        private static JObject ToNode(FileChange change)
        {
            var node = new JObject();
            foreach (var c in change.Changes.Where(c => c.Kind != ChangeKind.Removed))
                node[c.Key] = c.After!.DeepClone();
            return node;
        }

        // LF e sem BOM no repositório; mesma indentação que o mod grava
        private static string Serialize(JObject root) => root.ToString(Formatting.Indented) + "\n";
    }

    public sealed class SubmitResult
    {
        public bool Success { get; set; }
        public string? PullRequestUrl { get; set; }
        public string Message { get; set; } = "";
        public List<string> Warnings { get; } = new List<string>();
    }

    // Fluxo completo de envio:
    //   login → (fork) → HEAD da branch padrão do oficial → arquivos atuais →
    //   aplica as mudanças → tree + commit único → branch → PR.
    // Só chega aqui depois do usuário aprovar o preview na tela.
    public static class PullRequestSubmitter
    {
        public const int MaxFiles = 200;

        private static readonly Regex SafeLogin = new Regex("[^A-Za-z0-9-]", RegexOptions.Compiled);

        public static async Task<SubmitResult> SubmitAsync(
            IGitHubApi api, RepoRef upstream, ChangeSet changes, PullRequestContent content,
            DateTime nowUtc, CancellationToken ct)
        {
            var result = new SubmitResult();

            if (changes.IsEmpty || string.IsNullOrWhiteSpace(content.Title))
                return Fail(result, "Não há alterações para enviar.");
            if (changes.Files.Count > MaxFiles)
                return Fail(result, $"Alterações em {changes.Files.Count} arquivos; o limite por PR é {MaxFiles}. Envie em partes.");

            try
            {
                var login = await api.GetLoginAsync(ct).ConfigureAwait(false);
                var baseBranch = await api.GetDefaultBranchAsync(upstream, ct).ConfigureAwait(false);
                var baseCommit = await api.GetBranchHeadAsync(upstream, baseBranch, ct).ConfigureAwait(false);

                // arquivos novos do conteúdo: lidos da versão ATUAL do oficial
                var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var f in changes.Files)
                {
                    var current = await api.GetFileTextAsync(upstream, "translations/" + f.File, baseCommit, ct).ConfigureAwait(false);
                    var patched = PullRequestPatcher.Apply(current, f, result.Warnings);
                    if (patched != null) files["translations/" + f.File] = patched;
                }

                if (files.Count == 0)
                    return Fail(result, "O GitHub já tem todas essas traduções; nada a enviar.");

                var head = await api.EnsureForkAsync(upstream, login, ct).ConfigureAwait(false);
                var baseTree = await api.GetCommitTreeAsync(upstream, baseCommit, ct).ConfigureAwait(false);
                var tree = await api.CreateTreeAsync(head, baseTree, files, ct).ConfigureAwait(false);
                var commit = await api.CreateCommitAsync(head, content.CommitMessage, tree, baseCommit, ct).ConfigureAwait(false);

                var branch = BranchName(login, nowUtc);
                await api.CreateBranchAsync(head, branch, commit, ct).ConfigureAwait(false);

                var headRef = string.Equals(head.Owner, upstream.Owner, StringComparison.OrdinalIgnoreCase)
                    ? branch
                    : head.Owner + ":" + branch;

                result.PullRequestUrl = await api.CreatePullRequestAsync(upstream, content.Title, headRef, baseBranch, content.Body, ct).ConfigureAwait(false);
                result.Success = true;
                result.Message = "Pull Request criada.";
                return result;
            }
            catch (OperationCanceledException)
            {
                return Fail(result, "Envio cancelado.");
            }
            catch (GitHubApiException ex)
            {
                return Fail(result, ex.Message);
            }
            catch (Exception ex)
            {
                return Fail(result, "Falha ao enviar: " + ex.Message);
            }
        }

        internal static string BranchName(string login, DateTime nowUtc)
        {
            var safe = SafeLogin.Replace(login, "");
            if (safe.Length == 0) safe = "user";
            return $"translations/{safe}/{nowUtc:yyyyMMdd-HHmmss}";
        }

        private static SubmitResult Fail(SubmitResult r, string message)
        {
            r.Success = false; r.Message = message; return r;
        }
    }
}
