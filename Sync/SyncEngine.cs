using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    public enum FileSyncAction { Unchanged, Created, Updated, Deleted, Skipped }

    public sealed class FileSyncResult
    {
        public string File { get; set; } = "";
        public FileSyncAction Action { get; set; }
        public string? Reason { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
        public int KeptLocal { get; set; }
        public int Conflicts { get; set; }
    }

    public sealed class SyncReport
    {
        public bool DryRun { get; set; }
        public List<FileSyncResult> Files { get; } = new List<FileSyncResult>();
        public List<EntryConflict> Conflicts { get; } = new List<EntryConflict>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Conflitos cuja versão do usuário foi gravada em overrides/.</summary>
        public int OverridesWritten { get; set; }

        public int Changed => Files.Count(f => f.Action != FileSyncAction.Unchanged && f.Action != FileSyncAction.Skipped);
    }

    public sealed class SyncOptions
    {
        /// <summary>Pasta de traduções do usuário (BepInEx/plugins/translations).</summary>
        public string LocalRoot { get; set; } = "";
        /// <summary>Pasta com o pacote remoto já baixado e extraído (staging).</summary>
        public string RemoteRoot { get; set; } = "";
        public string? PackageVersion { get; set; }
        public bool DryRun { get; set; }
        public long MaxFileBytes { get; set; } = 8L * 1024 * 1024;
    }

    // Aplica um pacote remoto de traduções sobre a pasta local SEM perder edições:
    //   - por arquivo, faz merge 3-way por entrada (ThreeWayMerge) contra um
    //     snapshot BASE guardado em _sync/base/ (o pacote da última sincronização);
    //   - em conflito o remoto vence no arquivo de conteúdo e a versão do usuário
    //     vai pra overrides/sync_conflicts.json — a camada de override continua
    //     vencendo em runtime, então o jogador não vê sua tradução sumir;
    //   - só toca a camada de conteúdo; overrides/** e uncategorized.json nunca
    //     são sobrescritos pelo pacote remoto.
    //
    // Ordem de gravação (idempotente se o processo morrer no meio): overrides →
    // arquivos de conteúdo → snapshots base → state.json. Repetir a execução após
    // uma falha converge, porque "L == R" é sempre no-op.
    public static class SyncEngine
    {
        public const string StateFolder = "_sync";
        public const string ConflictsFile = "sync_conflicts.json";

        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

        private sealed class Plan
        {
            public string Rel = "";
            public string LocalPath = "";
            public string BasePath = "";
            public string? RemotePath;
            public JObject? NewRoot;        // null = apagar o arquivo local
            public bool WriteLocal;
            public FileSyncResult Result = new FileSyncResult();
        }

        public static SyncReport Apply(SyncOptions opt)
        {
            var report = new SyncReport { DryRun = opt.DryRun };
            var baseDir = Path.Combine(opt.LocalRoot, StateFolder, "base");

            // 1) quais arquivos do pacote remoto entram
            var remoteFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(opt.RemoteRoot))
            {
                foreach (var full in Directory.GetFiles(opt.RemoteRoot, "*.json", SearchOption.AllDirectories))
                {
                    var rel = ToRel(opt.RemoteRoot, full);
                    if (string.Equals(rel, "_manifest.json", StringComparison.OrdinalIgnoreCase)) continue;

                    var why = RejectReason(opt, rel, full);
                    if (why != null)
                    {
                        report.Files.Add(new FileSyncResult { File = rel, Action = FileSyncAction.Skipped, Reason = why });
                        report.Warnings.Add($"'{rel}' ignorado: {why}");
                        continue;
                    }
                    remoteFiles[rel] = full;
                }
            }

            // arquivos que existiam na sincronização anterior e sumiram do remoto
            var all = new SortedSet<string>(remoteFiles.Keys, StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(baseDir))
                foreach (var full in Directory.GetFiles(baseDir, "*.json", SearchOption.AllDirectories))
                    all.Add(ToRel(baseDir, full));

            // 2) plano em memória — nada é gravado ainda
            var plans = new List<Plan>();
            foreach (var rel in all)
            {
                var plan = PlanFile(opt, rel, remoteFiles.TryGetValue(rel, out var rp) ? rp : null, baseDir, report);
                if (plan != null) plans.Add(plan);
            }

            // 3) grava: overrides → conteúdo → base → state
            if (!opt.DryRun)
            {
                report.OverridesWritten = WriteConflictOverrides(opt.LocalRoot, report);

                foreach (var p in plans.Where(p => p.WriteLocal))
                {
                    if (p.NewRoot == null) { if (File.Exists(p.LocalPath)) File.Delete(p.LocalPath); }
                    else WriteJsonAtomic(p.LocalPath, p.NewRoot);
                }

                foreach (var p in plans)
                {
                    if (p.RemotePath != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(p.BasePath)!);
                        File.Copy(p.RemotePath, p.BasePath, overwrite: true);
                    }
                    else if (File.Exists(p.BasePath)) File.Delete(p.BasePath);
                }

                WriteState(opt);
            }

            report.Files.AddRange(plans.Select(p => p.Result));
            return report;
        }

        // ---------------------------------------------------------------- plano

        private static Plan? PlanFile(SyncOptions opt, string rel, string? remotePath, string baseDir, SyncReport report)
        {
            var plan = new Plan
            {
                Rel        = rel,
                LocalPath  = Path.Combine(opt.LocalRoot, FromRel(rel)),
                BasePath   = Path.Combine(baseDir, FromRel(rel)),
                RemotePath = remotePath,
                Result     = new FileSyncResult { File = rel },
            };

            JObject? rRoot, bRoot, lRoot;
            try
            {
                rRoot = remotePath != null ? ReadRoot(remotePath) : null;
                bRoot = File.Exists(plan.BasePath) ? ReadRoot(plan.BasePath) : null;
            }
            catch (Exception ex)
            {
                // remoto ilegível → não toca em nada deste arquivo; base corrompida
                // idem (sem base confiável o merge seria um chute)
                return Skip(report, rel, "JSON inválido no pacote remoto ou no snapshot base: " + ex.Message);
            }

            try { lRoot = File.Exists(plan.LocalPath) ? ReadRoot(plan.LocalPath) : null; }
            catch (Exception ex)
            {
                return Skip(report, rel, "arquivo local ilegível, não foi alterado: " + ex.Message);
            }

            var merge = ThreeWayMerge.Merge(
                rel,
                bRoot != null ? Node(bRoot) : new JObject(),
                lRoot != null ? Node(lRoot) : new JObject(),
                rRoot != null ? Node(rRoot) : new JObject());

            report.Conflicts.AddRange(merge.Conflicts);
            var r = plan.Result;
            r.Added = merge.Added; r.Updated = merge.Updated; r.Removed = merge.Removed;
            r.KeptLocal = merge.KeptLocal; r.Conflicts = merge.Conflicts.Count;

            if (rRoot == null && merge.Merged.Count == 0)
            {
                // o remoto apagou o arquivo e nada de valor do usuário ficou nele
                plan.NewRoot = null;
                plan.WriteLocal = lRoot != null;
                r.Action = lRoot != null ? FileSyncAction.Deleted : FileSyncAction.Unchanged;
                return plan;
            }

            // a estrutura ($meta, forma legada/com envelope) vem do remoto; se o
            // remoto apagou o arquivo mas o usuário tem coisa nele, usa a do local
            var template = rRoot ?? lRoot!;
            var newRoot = WithNode(template, merge.Merged);

            if (lRoot == null)
            {
                plan.NewRoot = newRoot; plan.WriteLocal = true; r.Action = FileSyncAction.Created;
            }
            else if (JToken.DeepEquals(Canonical(newRoot), Canonical(lRoot)))
            {
                r.Action = FileSyncAction.Unchanged;
            }
            else
            {
                plan.NewRoot = newRoot; plan.WriteLocal = true; r.Action = FileSyncAction.Updated;
            }
            return plan;
        }

        private static Plan? Skip(SyncReport report, string rel, string why)
        {
            report.Files.Add(new FileSyncResult { File = rel, Action = FileSyncAction.Skipped, Reason = why });
            report.Warnings.Add($"'{rel}': {why}");
            return null;
        }

        // ----------------------------------------------------------- validação

        // O pacote remoto é entrada não confiável: só arquivos de CONTEÚDO, dentro
        // da pasta, de tamanho razoável e sem links simbólicos.
        private static string? RejectReason(SyncOptions opt, string rel, string full)
        {
            var parts = rel.Split('/');
            if (parts.Any(s => s == ".." || s == "." || s.Length == 0 || s.IndexOf(':') >= 0))
                return "caminho inválido";
            if (parts.Any(s => s.StartsWith("_", StringComparison.Ordinal)))
                return "pasta/arquivo reservado (começa com '_')";
            if (string.Equals(parts[0], "overrides", StringComparison.OrdinalIgnoreCase))
                return "o pacote remoto não pode escrever na camada overrides/";
            if (parts.Length == 1 &&
                (string.Equals(parts[0], "uncategorized.json", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "npc_names.json", StringComparison.OrdinalIgnoreCase)))
                return "arquivo reservado da raiz";

            var fi = new FileInfo(full);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return "link simbólico";
            if (fi.Length > opt.MaxFileBytes) return $"maior que o limite ({opt.MaxFileBytes} bytes)";
            return null;
        }

        // ---------------------------------------------------- overrides/conflitos

        // Grava a versão do usuário dos conflitos em overrides/sync_conflicts.json.
        // Pula o que já está coberto por outro override (a edição do usuário lá já
        // vence) e o que colidiria com outro contexto da mesma chave.
        private static int WriteConflictOverrides(string localRoot, SyncReport report)
        {
            var toKeep = report.Conflicts.Where(c => c.Local != null).ToList();
            if (toKeep.Count == 0) return 0;

            var path = Path.Combine(localRoot, "overrides", ConflictsFile);
            var root = File.Exists(path) ? ReadRoot(path) : new JObject();
            var node = root["translations"] as JObject;
            if (node == null)
            {
                node = new JObject();
                root["translations"] = node;
                if (root["$meta"] == null)
                    root["$meta"] = new JObject { ["schemaVersion"] = "2.0", ["category"] = "overrides/sync_conflicts" };
            }

            var covered = CollectOverrideKeys(localRoot, path);
            int written = 0;
            foreach (var c in toKeep)
            {
                var ctx = EntryCanon.ContextOf(c.Local!);
                if (covered.Contains(Id(c.Key, ctx)))
                {
                    report.Warnings.Add($"conflito em '{c.Key}' ({c.File}): já existe override seu, mantido.");
                    continue;
                }
                if (node[c.Key] is JToken other && !string.Equals(EntryCanon.ContextOf(other), ctx, StringComparison.Ordinal))
                {
                    report.Warnings.Add($"conflito em '{c.Key}' ({c.File}): outro contexto já usa essa chave em {ConflictsFile}; versão sua NÃO guardada.");
                    continue;
                }
                node[c.Key] = c.Local!.DeepClone();
                written++;
            }

            if (written > 0) WriteJsonAtomic(path, root);
            return written;
        }

        private static HashSet<string> CollectOverrideKeys(string localRoot, string exceptFile)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<string>();
            var ovDir = Path.Combine(localRoot, "overrides");
            if (Directory.Exists(ovDir)) files.AddRange(Directory.GetFiles(ovDir, "*.json", SearchOption.AllDirectories));
            var unc = Path.Combine(localRoot, "uncategorized.json");
            if (File.Exists(unc)) files.Add(unc);

            foreach (var f in files)
            {
                if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(exceptFile), StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(f).StartsWith("_", StringComparison.Ordinal)) continue;
                try
                {
                    foreach (var p in Node(ReadRoot(f)).Properties())
                        set.Add(Id(p.Name, EntryCanon.ContextOf(p.Value)));
                }
                catch { /* override ilegível: o repositório também o ignora */ }
            }
            return set;
        }

        private static string Id(string key, string? ctx) => string.IsNullOrEmpty(ctx) ? key : key + "@@" + ctx;

        // -------------------------------------------------------------- estado

        private static void WriteState(SyncOptions opt)
        {
            var state = new JObject
            {
                ["packageVersion"] = opt.PackageVersion,
                ["syncedAtUtc"]    = DateTime.UtcNow.ToString("o"),
            };
            WriteJsonAtomic(Path.Combine(opt.LocalRoot, StateFolder, "state.json"), state);
        }

        // ------------------------------------------------------------- JSON I/O

        internal static JObject ReadRoot(string path) =>
            JObject.Parse(File.ReadAllText(path, Encoding.UTF8));

        /// <summary>Nó com as entradas: "translations", ou a raiz inteira (menos $meta) no formato legado.</summary>
        internal static JObject Node(JObject root)
        {
            if (root["translations"] is JObject t) return t;
            var flat = new JObject();
            foreach (var p in root.Properties().Where(p => p.Name != "$meta"))
                flat[p.Name] = p.Value.DeepClone();
            return flat;
        }

        internal static JObject WithNode(JObject template, JObject entries)
        {
            if (template["translations"] is JObject)
            {
                var clone = (JObject)template.DeepClone();
                clone["translations"] = entries.DeepClone();
                return clone;
            }
            var flat = new JObject();
            if (template["$meta"] != null) flat["$meta"] = template["$meta"]!.DeepClone();
            foreach (var p in entries.Properties()) flat[p.Name] = p.Value.DeepClone();
            return flat;
        }

        // compara arquivos ignorando grafia das entradas (string vs {text}) e ordem
        private static JObject Canonical(JObject root)
        {
            var o = new JObject();
            foreach (var p in root.Properties().Where(p => p.Name != "translations"))
                o[p.Name] = p.Value.DeepClone();
            var node = new JObject();
            foreach (var p in Node(root).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                node[p.Name] = EntryCanon.Canon(p.Value);
            o["translations"] = node;
            return o;
        }

        internal static void WriteJsonAtomic(string fullPath, JObject root)
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = fullPath + ".tmp";
            File.WriteAllText(tmp, root.ToString(Formatting.Indented), Utf8Bom);
            if (File.Exists(fullPath)) File.Replace(tmp, fullPath, fullPath + ".bak");
            else File.Move(tmp, fullPath);
        }

        private static string ToRel(string root, string full)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Path.GetFullPath(full).Substring(r.Length).Replace('\\', '/');
        }

        private static string FromRel(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);
    }
}
