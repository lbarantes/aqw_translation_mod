using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

using AQWMod.Localization.Sync;

namespace AQWMod.Localization.GitHubPr
{
    public sealed class ComposeOptions
    {
        public string Locale { get; set; } = "pt-BR";
        public string ModVersion { get; set; } = "";
        public string Edition { get; set; } = "";
        public int MaxTitleLength { get; set; } = 110;
        /// <summary>Limite do corpo (o GitHub aceita 65.536 caracteres).</summary>
        public int MaxBodyLength { get; set; } = 56000;
        public int MaxEntriesPerFile { get; set; } = 40;
        public int MaxIssuesListed { get; set; } = 25;
    }

    public sealed class PullRequestContent
    {
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string CommitMessage { get; set; } = "";
        public List<LintIssue> Issues { get; } = new List<LintIssue>();
        public int Added { get; set; }
        public int Modified { get; set; }
        public int Removed { get; set; }
        public int FileCount { get; set; }
    }

    // Monta título e descrição da PR a partir do diff local — sem rede e sem IA,
    // só regras: o mesmo conjunto de mudanças gera sempre o mesmo texto.
    //
    // Categoria vem do primeiro segmento do caminho (maps/, skills/, ui/...);
    // a "área" é o resto do caminho sem a extensão (ex.: maps/Pirates/rhuibarb.json
    // -> categoria "maps", área "Pirates/rhuibarb"). Arquivos na raiz caem em "geral".
    //
    // Todo texto de tradução/chave/caminho entra dentro de code spans ou
    // escapado: um "@alguem" ou "#12" dentro de uma tradução não pode virar
    // menção nem link na PR.
    public static class PullRequestComposer
    {
        public static PullRequestContent Compose(ChangeSet changes, ComposeOptions opt)
        {
            var pr = new PullRequestContent
            {
                Added = changes.Added, Modified = changes.Modified, Removed = changes.Removed,
                FileCount = changes.Files.Count,
            };
            if (changes.IsEmpty) return pr;

            var files = changes.Files.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var f in files)
                foreach (var c in f.Changes)
                    pr.Issues.AddRange(EntryLint.Check(f.File, c));

            pr.Title = BuildTitle(files, pr, opt);
            pr.Body  = BuildBody(files, changes, pr, opt);
            pr.CommitMessage = pr.Title;
            return pr;
        }

        // ------------------------------------------------------------- categoria

        internal static string CategoryOf(string rel)
        {
            var i = rel.IndexOf('/');
            return i < 0 ? "geral" : rel.Substring(0, i);
        }

        internal static string AreaOf(string rel)
        {
            var i = rel.IndexOf('/');
            var rest = i < 0 ? rel : rel.Substring(i + 1);
            return rest.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? rest.Substring(0, rest.Length - 5) : rest;
        }

        // ---------------------------------------------------------------- título

        private static string BuildTitle(List<FileChange> files, PullRequestContent pr, ComposeOptions opt)
        {
            var groups = files
                .GroupBy(f => CategoryOf(f.File), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Cat: g.Key, Areas: g.Select(f => AreaOf(f.File)).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();

            var counts = $"+{pr.Added}" + (pr.Modified > 0 ? $" ~{pr.Modified}" : "") + (pr.Removed > 0 ? $" -{pr.Removed}" : "");
            var prefix = $"[{opt.Locale}] ";
            var suffix = $" ({counts})";

            var parts = new List<string>();
            int hidden = 0;
            foreach (var g in groups)
            {
                var shown = g.Areas.Take(3).ToList();
                var more = g.Areas.Count - shown.Count;
                parts.Add($"{g.Cat}: {string.Join(", ", shown)}" + (more > 0 ? $" +{more}" : ""));
            }

            // corta categorias inteiras até caber, sinalizando quantas ficaram de fora
            string Join(int n) => string.Join(" · ", parts.Take(n)) + (n < parts.Count ? $" · +{parts.Count - n} categorias" : "");
            var count = parts.Count;
            while (count > 1 && (prefix + Join(count) + suffix).Length > opt.MaxTitleLength) { count--; hidden++; }

            var title = prefix + Join(count) + suffix;
            if (title.Length > opt.MaxTitleLength)
                title = title.Substring(0, opt.MaxTitleLength - 1) + "…";
            return title;
        }

        // ----------------------------------------------------------------- corpo

        private static string BuildBody(List<FileChange> files, ChangeSet changes, PullRequestContent pr, ComposeOptions opt)
        {
            var sb = new StringBuilder();

            sb.AppendLine("## Resumo");
            sb.AppendLine();
            sb.Append($"Esta PR adiciona **{pr.Added}** e altera **{pr.Modified}** tradução(ões)");
            if (pr.Removed > 0) sb.Append($", removendo **{pr.Removed}**");
            sb.AppendLine($" em **{files.Count}** arquivo(s) ({Html(opt.Locale)}).");
            sb.AppendLine();

            sb.AppendLine("| Categoria | Arquivos | Novas | Alteradas | Removidas |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var g in files.GroupBy(f => CategoryOf(f.File), StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"| {Cell(g.Key)} | {g.Count()} | {g.Sum(f => f.Added)} | {g.Sum(f => f.Modified)} | {g.Sum(f => f.Removed)} |");
            sb.AppendLine();

            AppendIssues(sb, pr, opt);

            sb.AppendLine("## Alterações por arquivo");
            sb.AppendLine();

            int notListed = 0;
            foreach (var f in files)
            {
                if (sb.Length > opt.MaxBodyLength) { notListed += f.Changes.Count; continue; }
                AppendFile(sb, f, opt, ref notListed);
            }
            if (notListed > 0)
                sb.AppendLine($"_Lista truncada: {notListed} alteração(ões) não detalhadas aqui; veja o diff da PR._").AppendLine();

            if (changes.IgnoredRemovals > 0)
                sb.AppendLine($"_{changes.IgnoredRemovals} remoção(ões) locais não foram incluídas nesta PR._").AppendLine();

            sb.AppendLine("---");
            var footer = new List<string> { "Gerada pelo mod" };
            if (!string.IsNullOrEmpty(opt.ModVersion)) footer.Add($"AQWTranslation {Html(opt.ModVersion)}" + (opt.Edition.Length > 0 ? $" ({Html(opt.Edition)})" : ""));
            if (!string.IsNullOrEmpty(changes.BaseVersion)) footer.Add($"base {Html(changes.BaseVersion!)}");
            sb.AppendLine("<sub>" + string.Join(" · ", footer) + "</sub>");

            return sb.ToString();
        }

        private static void AppendIssues(StringBuilder sb, PullRequestContent pr, ComposeOptions opt)
        {
            if (pr.Issues.Count == 0) return;

            sb.AppendLine("## Pontos de atenção para a revisão");
            sb.AppendLine();

            void Section(LintKind kind, string label)
            {
                var items = pr.Issues.Where(i => i.Kind == kind).ToList();
                if (items.Count == 0) return;
                sb.AppendLine($"**{label}** ({items.Count})");
                foreach (var i in items.Take(opt.MaxIssuesListed))
                    sb.AppendLine($"- {Code(i.Key, 70)} em {Code(i.File, 80)}: {Html(i.Detail)}");
                if (items.Count > opt.MaxIssuesListed)
                    sb.AppendLine($"- … e mais {items.Count - opt.MaxIssuesListed}");
                sb.AppendLine();
            }

            Section(LintKind.PlaceholderMismatch, "Placeholders diferentes do original");
            Section(LintKind.TagMismatch, "Tags de formatação diferentes do original");
            Section(LintKind.EmptyTranslation, "Traduções vazias");
            Section(LintKind.SameAsOriginal, "Iguais ao texto original");
            Section(LintKind.MachineTranslated, "Tradução automática (revisar com atenção)");
        }

        private static void AppendFile(StringBuilder sb, FileChange f, ComposeOptions opt, ref int notListed)
        {
            var bits = new List<string>();
            if (f.Added > 0)    bits.Add($"{f.Added} nova(s)");
            if (f.Modified > 0) bits.Add($"{f.Modified} alterada(s)");
            if (f.Removed > 0)  bits.Add($"{f.Removed} removida(s)");

            sb.AppendLine($"<details><summary><code>{Html(f.File)}</code> — {string.Join(", ", bits)}{(f.IsNewFile ? " (arquivo novo)" : "")}</summary>");
            sb.AppendLine();

            int listed = 0;
            foreach (var kind in new[] { ChangeKind.Added, ChangeKind.Modified, ChangeKind.Removed })
            {
                var items = f.Changes.Where(c => c.Kind == kind).ToList();
                if (items.Count == 0) continue;

                sb.AppendLine(kind == ChangeKind.Added ? "**Novas**" : kind == ChangeKind.Modified ? "**Alteradas**" : "**Removidas**");
                foreach (var c in items)
                {
                    if (listed >= opt.MaxEntriesPerFile) { notListed++; continue; }
                    listed++;
                    sb.AppendLine("- " + Line(c));
                }
                sb.AppendLine();
            }
            if (notListed > 0 && listed >= opt.MaxEntriesPerFile)
                sb.AppendLine("_… mais entradas neste arquivo; veja o diff._").AppendLine();

            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        private static string Line(EntryChange c)
        {
            string Mark(JToken? t) => t != null && EntryLint.IsIgnored(t) ? " (ignorada)" : "";
            switch (c.Kind)
            {
                case ChangeKind.Added:
                    return $"{Code(c.Key, 60)} → {Code(Shown(c.After!), 80)}{Mark(c.After)}";
                case ChangeKind.Modified:
                    return $"{Code(c.Key, 60)}: {Code(Shown(c.Before!), 60)} → {Code(Shown(c.After!), 60)}{Mark(c.After)}";
                default:
                    return $"{Code(c.Key, 60)} (era {Code(Shown(c.Before!), 60)})";
            }
        }

        private static string Shown(Newtonsoft.Json.Linq.JToken entry) =>
            EntryLint.IsIgnored(entry) ? "(não traduzir)" : EntryLint.TextOf(entry);

        // ------------------------------------------------------------- escapes

        /// <summary>Texto vindo do jogo/usuário, sempre dentro de um code span.</summary>
        internal static string Code(string s, int max)
        {
            s = s.Replace("\r\n", "↵").Replace("\n", "↵").Replace("\r", "↵").Replace("`", "'");
            if (s.Length > max) s = s.Substring(0, max - 1) + "…";
            return s.Length == 0 ? "``" : "`" + s + "`";
        }

        internal static string Html(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // célula de tabela: sem '|' nem quebra de linha
        private static string Cell(string s) => Html(s).Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
    }
}
