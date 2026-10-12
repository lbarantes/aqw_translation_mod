using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

using AQWMod.Localization.Sync;

namespace AQWMod.Localization.GitHubPr
{
    public enum ChangeKind { Added, Modified, Removed }

    public sealed class EntryChange
    {
        public string Key { get; set; } = "";
        public ChangeKind Kind { get; set; }
        /// <summary>Valor no snapshot base (null em Added).</summary>
        public JToken? Before { get; set; }
        /// <summary>Valor local (null em Removed).</summary>
        public JToken? After { get; set; }
    }

    public sealed class FileChange
    {
        public string File { get; set; } = "";
        /// <summary>O arquivo não existe no pacote do GitHub (não tem snapshot base).</summary>
        public bool IsNewFile { get; set; }
        /// <summary>Conteúdo completo do arquivo local (usado quando o arquivo não existe no GitHub).</summary>
        public JObject? LocalRoot { get; set; }
        public List<EntryChange> Changes { get; } = new List<EntryChange>();

        public int Added    => Changes.Count(c => c.Kind == ChangeKind.Added);
        public int Modified => Changes.Count(c => c.Kind == ChangeKind.Modified);
        public int Removed  => Changes.Count(c => c.Kind == ChangeKind.Removed);
    }

    public sealed class ChangeSet
    {
        /// <summary>Nunca houve sincronização: não existe base pra comparar.</summary>
        public bool NeverSynced { get; set; }
        public string? BaseVersion { get; set; }
        public List<FileChange> Files { get; } = new List<FileChange>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>Remoções locais deixadas de fora (por padrão não vão na PR).</summary>
        public int IgnoredRemovals { get; set; }

        public int Added    => Files.Sum(f => f.Added);
        public int Modified => Files.Sum(f => f.Modified);
        public int Removed  => Files.Sum(f => f.Removed);
        public bool IsEmpty => Files.Count == 0;
    }

    // O que o usuário criou ou alterou localmente e o GitHub ainda não tem:
    // diferença entre cada arquivo de CONTEÚDO local e o snapshot base
    // (_sync/base/, o pacote da última sincronização). É a lista que vira PR.
    //
    // Só olha arquivos de conteúdo (mesma regra do SyncEngine): overrides/**,
    // uncategorized.json e npc_names.json ficam de fora — quando as entradas de
    // uncategorized.json forem movidas pras categorias, passam a aparecer aqui.
    public static class LocalChanges
    {
        public static ChangeSet Compute(string localRoot, bool includeRemovals = false)
        {
            var set = new ChangeSet();
            var syncDir = Path.Combine(localRoot, SyncEngine.StateFolder);
            var baseDir = Path.Combine(syncDir, "base");

            var statePath = Path.Combine(syncDir, "state.json");
            if (!File.Exists(statePath))
            {
                set.NeverSynced = true;
                set.Warnings.Add("Nenhuma sincronização com o GitHub aconteceu ainda; sem base para comparar.");
                return set;
            }
            try { set.BaseVersion = (string?)JObject.Parse(File.ReadAllText(statePath))["packageVersion"]; }
            catch { /* estado ilegível: segue sem versão */ }

            var rels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(localRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = ToRel(localRoot, f);
                if (SyncPaths.RejectReason(rel) == null) rels.Add(rel);
            }
            if (Directory.Exists(baseDir))
                foreach (var f in Directory.GetFiles(baseDir, "*.json", SearchOption.AllDirectories))
                    rels.Add(ToRel(baseDir, f));

            foreach (var rel in rels)
            {
                var localPath = Path.Combine(localRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                var basePath  = Path.Combine(baseDir,  rel.Replace('/', Path.DirectorySeparatorChar));

                JObject? l, b;
                try
                {
                    l = File.Exists(localPath) ? SyncEngine.ReadRoot(localPath) : null;
                    b = File.Exists(basePath)  ? SyncEngine.ReadRoot(basePath)  : null;
                }
                catch (Exception ex)
                {
                    set.Warnings.Add($"'{rel}' ignorado (JSON inválido): {ex.Message}");
                    continue;
                }

                var ln = l != null ? SyncEngine.Node(l) : new JObject();
                var bn = b != null ? SyncEngine.Node(b) : new JObject();
                var fc = new FileChange { File = rel, IsNewFile = b == null, LocalRoot = l };

                foreach (var p in ln.Properties())
                {
                    var before = bn[p.Name];
                    if (before == null)
                        fc.Changes.Add(new EntryChange { Key = p.Name, Kind = ChangeKind.Added, After = p.Value.DeepClone() });
                    else if (!EntryCanon.Same(before, p.Value))
                        fc.Changes.Add(new EntryChange { Key = p.Name, Kind = ChangeKind.Modified, Before = before.DeepClone(), After = p.Value.DeepClone() });
                }

                var removed = bn.Properties().Where(p => ln[p.Name] == null).ToList();
                if (includeRemovals)
                    foreach (var p in removed)
                        fc.Changes.Add(new EntryChange { Key = p.Name, Kind = ChangeKind.Removed, Before = p.Value.DeepClone() });
                else
                    set.IgnoredRemovals += removed.Count;

                if (fc.Changes.Count > 0) set.Files.Add(fc);
            }

            return set;
        }

        private static string ToRel(string root, string full)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Path.GetFullPath(full).Substring(r.Length).Replace('\\', '/');
        }
    }
}
