using System;
using System.IO;
using System.Linq;
using AQWMod.Localization.GitHubPr;
using AQWMod.Localization.Sync;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class LocalChangesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw_chg_" + Guid.NewGuid().ToString("N"));
        public LocalChangesTests() => Directory.CreateDirectory(_root);
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        private static string Doc(params (string k, string v)[] e) => TestPackage.TranslationsJson(e);

        private void Local(string rel, string json) => Put(_root, rel, json);
        private void Base(string rel, string json) => Put(Path.Combine(_root, "_sync", "base"), rel, json);

        private static void Put(string root, string rel, string json)
        {
            var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, json);
        }

        private void State(string version = "2026.09.30") => Put(Path.Combine(_root, "_sync"), "state.json", $"{{\"packageVersion\":\"{version}\"}}");

        [Fact]
        public void NeverSynced_ReportsItInsteadOfListingEverything()
        {
            Local("ui/hud.json", Doc(("Play", "Jogar")));
            var cs = LocalChanges.Compute(_root);
            Assert.True(cs.NeverSynced);
            Assert.True(cs.IsEmpty);
        }

        [Fact]
        public void IdenticalToBase_HasNoChanges()
        {
            State();
            Base("ui/hud.json", Doc(("Play", "Jogar")));
            Local("ui/hud.json", Doc(("Play", "Jogar")));
            Assert.True(LocalChanges.Compute(_root).IsEmpty);
        }

        [Fact]
        public void AddedAndModifiedEntries_AreDetected()
        {
            State();
            Base("ui/hud.json", Doc(("Play", "Jogar"), ("Quit", "Sair")));
            Local("ui/hud.json", Doc(("Play", "Começar"), ("Quit", "Sair"), ("Help", "Ajuda")));

            var cs = LocalChanges.Compute(_root);
            var f = Assert.Single(cs.Files);
            Assert.Equal(1, f.Added);
            Assert.Equal(1, f.Modified);
            Assert.Equal("Jogar", (string)f.Changes.Single(c => c.Kind == ChangeKind.Modified).Before!);
            Assert.Equal("2026.09.30", cs.BaseVersion);
        }

        [Fact]
        public void LocalOnlyFile_IsNewFile_WithAllEntriesAdded()
        {
            State();
            Local("classes/mine.json", Doc(("A", "a"), ("B", "b")));
            var f = Assert.Single(LocalChanges.Compute(_root).Files);
            Assert.True(f.IsNewFile);
            Assert.Equal(2, f.Added);
        }

        [Fact]
        public void Removals_AreLeftOut_UnlessRequested()
        {
            State();
            Base("ui/hud.json", Doc(("Play", "Jogar"), ("Quit", "Sair")));
            Local("ui/hud.json", Doc(("Play", "Jogar")));

            var def = LocalChanges.Compute(_root);
            Assert.True(def.IsEmpty);
            Assert.Equal(1, def.IgnoredRemovals);

            var inc = LocalChanges.Compute(_root, includeRemovals: true);
            Assert.Equal(1, inc.Removed);
        }

        [Fact]
        public void OverridesAndUncategorized_AreNotPartOfThePr()
        {
            State();
            Local("uncategorized.json", Doc(("X", "x")));
            Local("overrides/sync_conflicts.json", Doc(("Y", "y")));
            Local("_capture/ui.json", Doc(("Z", "z")));
            Assert.True(LocalChanges.Compute(_root).IsEmpty);
        }

        [Fact]
        public void ReformattingOnly_IsNotAChange()
        {
            State();
            Base("ui/hud.json", Doc(("Play", "Jogar")));
            Local("ui/hud.json", "{\"translations\":{\"Play\":{\"text\":\"Jogar\",\"ignore\":false}}}");
            Assert.True(LocalChanges.Compute(_root).IsEmpty);
        }

        [Fact]
        public void BrokenLocalFile_IsReportedAndSkipped()
        {
            State();
            Local("ui/hud.json", "{ nope");
            Local("ui/ok.json", Doc(("A", "a")));
            var cs = LocalChanges.Compute(_root);
            Assert.Single(cs.Files);
            Assert.Contains(cs.Warnings, w => w.Contains("ui/hud.json"));
        }
    }

    public class EntryLintTests
    {
        private static EntryChange Added(string key, JToken after) =>
            new EntryChange { Key = key, Kind = ChangeKind.Added, After = after };

        private static LintKind[] Kinds(string key, JToken after) =>
            EntryLint.Check("f.json", Added(key, after)).Select(i => i.Kind).ToArray();

        [Fact] public void CleanEntry_HasNoIssues() => Assert.Empty(Kinds("Attack {player}", "Ataque {player}"));

        [Fact] public void EmptyTranslation_IsFlagged() =>
            Assert.Equal(new[] { LintKind.EmptyTranslation }, Kinds("Attack", "  "));

        [Fact] public void MissingPlaceholder_IsFlagged() =>
            Assert.Contains(LintKind.PlaceholderMismatch, Kinds("Hit {n1} for {n2}", "Acerte {n1}"));

        [Fact] public void ExtraPlaceholder_IsFlagged() =>
            Assert.Contains(LintKind.PlaceholderMismatch, Kinds("Hit", "Acerte {n1}"));

        [Fact] public void RepeatedPlaceholder_IsCountedAsMultiset() =>
            Assert.Contains(LintKind.PlaceholderMismatch, Kinds("{n1} and {n1}", "{n1}"));

        [Fact] public void ReorderedPlaceholders_AreFine() =>
            Assert.Empty(Kinds("{player} hits {npc}", "{npc} apanha de {player}"));

        [Fact] public void MismatchedColorTag_IsFlagged() =>
            Assert.Contains(LintKind.TagMismatch, Kinds("<color=#f00>Hot</color>", "<color=#f00>Quente"));

        [Fact] public void TagAttributesMayChange_NameMustMatch() =>
            Assert.Empty(Kinds("<color=#f00>Hot</color>", "<color=#ff0000>Quente</color>"));

        [Fact] public void SameAsOriginal_IsFlagged_UnlessIgnored()
        {
            Assert.Contains(LintKind.SameAsOriginal, Kinds("Wizard", "Wizard"));
            Assert.Empty(Kinds("Wizard", JObject.Parse("{'text':'Wizard','ignore':true}")));
        }

        [Fact] public void AutoSource_IsFlaggedAsMachineTranslated() =>
            Assert.Contains(LintKind.MachineTranslated, Kinds("Attack", JObject.Parse("{'text':'Ataque','source':'auto'}")));
    }

    public class PullRequestComposerTests
    {
        private static ChangeSet Set(params FileChange[] files)
        {
            var cs = new ChangeSet { BaseVersion = "2026.09.30" };
            cs.Files.AddRange(files);
            return cs;
        }

        private static FileChange File(string path, bool isNew, params EntryChange[] c)
        {
            var f = new FileChange { File = path, IsNewFile = isNew };
            f.Changes.AddRange(c);
            return f;
        }

        private static EntryChange Add(string k, string v) =>
            new EntryChange { Key = k, Kind = ChangeKind.Added, After = new JValue(v) };

        private static EntryChange Mod(string k, string before, string after) =>
            new EntryChange { Key = k, Kind = ChangeKind.Modified, Before = new JValue(before), After = new JValue(after) };

        private static readonly ComposeOptions Opt = new ComposeOptions { Locale = "pt-BR", ModVersion = "2.2.0", Edition = "Translator" };

        [Fact]
        public void EmptyChangeSet_ProducesNothing()
        {
            var pr = PullRequestComposer.Compose(new ChangeSet(), Opt);
            Assert.Equal("", pr.Title);
            Assert.Equal("", pr.Body);
        }

        [Fact]
        public void Title_SummarisesCategoriesAreasAndCounts()
        {
            var pr = PullRequestComposer.Compose(Set(
                File("maps/Pirates/rhuibarb.json", false, Add("A", "a"), Add("B", "b")),
                File("maps/pirates.json", false, Mod("C", "c", "c2")),
                File("ui/hud.json", false, Add("D", "d"))), Opt);

            Assert.Equal("[pt-BR] maps: pirates, Pirates/rhuibarb · ui: hud (+3 ~1)", pr.Title);
        }

        [Fact]
        public void Title_IsCappedAndSignalsHiddenCategories()
        {
            var files = Enumerable.Range(0, 30)
                .Select(i => File($"cat{i:00}/some_long_area_name_{i}.json", false, Add("k", "v"))).ToArray();
            var pr = PullRequestComposer.Compose(Set(files), Opt);

            Assert.True(pr.Title.Length <= Opt.MaxTitleLength);
            Assert.Contains("categorias", pr.Title);
            Assert.EndsWith("(+30)", pr.Title);
        }

        [Fact]
        public void RootFile_FallsIntoGeneralCategory()
        {
            var pr = PullRequestComposer.Compose(Set(File("generics.json", false, Add("A", "a"))), Opt);
            Assert.Contains("geral: generics", pr.Title);
        }

        [Fact]
        public void Body_HasSummaryTableFilesAndFooter()
        {
            var pr = PullRequestComposer.Compose(Set(
                File("ui/hud.json", false, Add("Play", "Jogar"), Mod("Quit", "Sair", "Fechar"))), Opt);

            Assert.Contains("## Resumo", pr.Body);
            Assert.Contains("| ui | 1 | 1 | 1 | 0 |", pr.Body);
            Assert.Contains("<code>ui/hud.json</code>", pr.Body);
            Assert.Contains("`Play` → `Jogar`", pr.Body);
            Assert.Contains("`Quit`: `Sair` → `Fechar`", pr.Body);
            Assert.Contains("AQWTranslation 2.2.0 (Translator)", pr.Body);
            Assert.Contains("base 2026.09.30", pr.Body);
        }

        [Fact]
        public void Body_ListsLintIssues_AndMachineTranslation()
        {
            var auto = new EntryChange { Key = "Attack", Kind = ChangeKind.Added, After = JObject.Parse("{'text':'Ataque','source':'auto'}") };
            var pr = PullRequestComposer.Compose(Set(
                File("skills/x.json", false, Add("Hit {n1}", "Acerte"), Add("Empty", ""), auto)), Opt);

            Assert.Contains("## Pontos de atenção", pr.Body);
            Assert.Contains("Placeholders diferentes", pr.Body);
            Assert.Contains("Traduções vazias", pr.Body);
            Assert.Contains("Tradução automática", pr.Body);
            Assert.Equal(3, pr.Issues.Count);
        }

        [Fact]
        public void NoIssues_NoAttentionSection() =>
            Assert.DoesNotContain("Pontos de atenção",
                PullRequestComposer.Compose(Set(File("ui/hud.json", false, Add("Play", "Jogar"))), Opt).Body);

        [Fact]
        public void MentionsLinksAndHtml_InTranslations_AreNeutralised()
        {
            var pr = PullRequestComposer.Compose(Set(File("ui/hud.json", false,
                Add("Hi @octocat fixes #12", "Oi @octocat </details><script>x</script> [a](http://evil)"))), Opt);

            // todo texto vindo da tradução está dentro de code spans
            Assert.Contains("`Hi @octocat fixes #12`", pr.Body);
            Assert.Contains("`Oi @octocat </details><script>x</script> [a](http://evil)`", pr.Body);
            // nada do texto vaza fora de um code span
            var withoutCode = System.Text.RegularExpressions.Regex.Replace(pr.Body, "`[^`]*`", "");
            Assert.DoesNotContain("@octocat", withoutCode);
            Assert.DoesNotContain("<script>", withoutCode);
            Assert.DoesNotContain("evil", withoutCode);
        }

        [Fact]
        public void BackticksAndNewlines_CannotBreakOutOfCodeSpan()
        {
            var pr = PullRequestComposer.Compose(Set(File("ui/hud.json", false, Add("a`b\nc", "x`y\r\nz"))), Opt);
            Assert.Contains("`a'b↵c`", pr.Body);
            Assert.Contains("`x'y↵z`", pr.Body);
        }

        [Fact]
        public void FilePath_IsHtmlEscaped()
        {
            var pr = PullRequestComposer.Compose(Set(File("ui/<b>&x.json", false, Add("A", "a"))), Opt);
            Assert.Contains("<code>ui/&lt;b&gt;&amp;x.json</code>", pr.Body);
        }

        [Fact]
        public void HugeChangeSet_StaysUnderBodyLimit_AndSaysTruncated()
        {
            var many = Enumerable.Range(0, 400)
                .Select(f => File($"maps/m{f:000}.json", false,
                    Enumerable.Range(0, 40).Select(i => Add($"Entry number {f}-{i} with a fairly long original text", "Uma tradução bastante longa de exemplo")).ToArray()))
                .ToArray();
            var pr = PullRequestComposer.Compose(Set(many), Opt);

            Assert.True(pr.Body.Length < 65000, "body = " + pr.Body.Length);
            Assert.Contains("Lista truncada", pr.Body);
        }

        [Fact]
        public void ManyEntriesInOneFile_AreCapped()
        {
            var entries = Enumerable.Range(0, 100).Select(i => Add("K" + i, "V" + i)).ToArray();
            var pr = PullRequestComposer.Compose(Set(File("ui/hud.json", false, entries)), Opt);

            Assert.Contains("`K0`", pr.Body);
            Assert.DoesNotContain("`K99`", pr.Body);
            Assert.Contains("veja o diff", pr.Body);
        }

        [Fact]
        public void IgnoredEntries_AreShownAsDoNotTranslate()
        {
            var e = new EntryChange { Key = "Wizard", Kind = ChangeKind.Added, After = JObject.Parse("{'text':'Wizard','ignore':true}") };
            var pr = PullRequestComposer.Compose(Set(File("ui/hud.json", false, e)), Opt);
            Assert.Contains("(não traduzir)", pr.Body);
            Assert.Contains("(ignorada)", pr.Body);
        }

        [Fact]
        public void SameInput_ProducesSameOutput()
        {
            ChangeSet Build() => Set(
                File("ui/hud.json", false, Add("A", "a")),
                File("maps/x.json", false, Add("B", "b")));
            var a = PullRequestComposer.Compose(Build(), Opt);
            var b = PullRequestComposer.Compose(Build(), Opt);
            Assert.Equal(a.Title, b.Title);
            Assert.Equal(a.Body, b.Body);
        }

        [Fact]
        public void FilesAreOrdered_NotByInputOrder()
        {
            var pr = PullRequestComposer.Compose(Set(
                File("ui/b.json", false, Add("A", "a")),
                File("maps/a.json", false, Add("B", "b"))), Opt);
            Assert.True(pr.Body.IndexOf("maps/a.json") < pr.Body.IndexOf("ui/b.json"));
        }
    }
}
