using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

using AQWMod.Localization.Sync;

namespace AQWMod.Localization.GitHubPr
{
    public enum LintKind
    {
        EmptyTranslation,
        PlaceholderMismatch,
        TagMismatch,
        SameAsOriginal,
        MachineTranslated,
    }

    public sealed class LintIssue
    {
        public string File { get; set; } = "";
        public string Key { get; set; } = "";
        public LintKind Kind { get; set; }
        public string Detail { get; set; } = "";
    }

    // Verificações baratas e determinísticas sobre uma entrada nova/alterada,
    // pra mostrar ao revisor da PR onde olhar com mais atenção. Nada aqui bloqueia
    // o envio — são avisos.
    public static class EntryLint
    {
        // {player}, {player2}, {npc}, {n1}, {map}, {quest}...
        private static readonly Regex PlaceholderRe = new Regex(@"\{[A-Za-z]+\d*\}", RegexOptions.Compiled);
        // nome da tag de rich text, sem atributos: <color=#f00> -> color, </b> -> /b
        private static readonly Regex TagRe = new Regex(@"<(/?[A-Za-z]+)[^>]*>", RegexOptions.Compiled);

        public static string TextOf(JToken entry) =>
            entry is JObject o ? ((string?)o["text"] ?? "") : (entry.Type == JTokenType.String ? (string)entry! : "");

        public static bool IsIgnored(JToken entry) =>
            entry is JObject o && o["ignore"]?.Type == JTokenType.Boolean && (bool)o["ignore"]!;

        public static string? SourceOf(JToken entry) =>
            entry is JObject o && o["source"]?.Type == JTokenType.String ? (string?)o["source"] : null;

        public static List<LintIssue> Check(string file, EntryChange change)
        {
            var issues = new List<LintIssue>();
            if (change.After == null) return issues;

            var key  = change.Key;
            var text = TextOf(change.After);

            // "não traduzir": o texto é o próprio original por definição
            if (IsIgnored(change.After)) return issues;

            if (string.IsNullOrWhiteSpace(text))
            {
                issues.Add(Issue(file, key, LintKind.EmptyTranslation, "tradução vazia"));
                return issues;
            }

            if (string.Equals(text, key, StringComparison.Ordinal))
                issues.Add(Issue(file, key, LintKind.SameAsOriginal,
                    "igual ao original (se for de propósito, marque como ignorada)"));

            var missing = Diff(Tokens(PlaceholderRe, key), Tokens(PlaceholderRe, text));
            var extra   = Diff(Tokens(PlaceholderRe, text), Tokens(PlaceholderRe, key));
            if (missing.Count > 0 || extra.Count > 0)
            {
                var parts = new List<string>();
                if (missing.Count > 0) parts.Add("faltando " + string.Join(", ", missing));
                if (extra.Count > 0)   parts.Add("a mais " + string.Join(", ", extra));
                issues.Add(Issue(file, key, LintKind.PlaceholderMismatch, "placeholders: " + string.Join("; ", parts)));
            }

            var tagMissing = Diff(Tokens(TagRe, key, 1), Tokens(TagRe, text, 1));
            var tagExtra   = Diff(Tokens(TagRe, text, 1), Tokens(TagRe, key, 1));
            if (tagMissing.Count > 0 || tagExtra.Count > 0)
                issues.Add(Issue(file, key, LintKind.TagMismatch, "tags de formatação diferentes do original"));

            if (string.Equals(SourceOf(change.After), "auto", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue(file, key, LintKind.MachineTranslated, "tradução automática, ainda sem revisão humana"));

            return issues;
        }

        // multiconjunto: "{n1} {n1}" difere de "{n1}"
        private static List<string> Tokens(Regex re, string s, int group = 0) =>
            re.Matches(s).Cast<Match>().Select(m => m.Groups[group].Value).OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>Itens de a que não têm par em b (respeitando repetições).</summary>
        private static List<string> Diff(List<string> a, List<string> b)
        {
            var pool = new List<string>(b);
            var result = new List<string>();
            foreach (var x in a)
            {
                var i = pool.IndexOf(x);
                if (i >= 0) pool.RemoveAt(i); else result.Add(x);
            }
            return result;
        }

        private static LintIssue Issue(string file, string key, LintKind kind, string detail) =>
            new LintIssue { File = file, Key = key, Kind = kind, Detail = detail };
    }
}
