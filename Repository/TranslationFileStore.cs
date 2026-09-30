using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Repository
{
    // Lê/escreve UM arquivo de tradução qualquer do corpus (não só
    // uncategorized.json), preservando $meta e os campos que a GUI não está
    // editando no momento. TranslationRepository é só-leitura e pensa no
    // corpus inteiro já mesclado (hotpath de tradução); esta classe pensa
    // em um arquivo por vez, pra Biblioteca poder editar qualquer um deles.
    // Escrita sempre atômica (temp + File.Replace/Move + .bak).
    public static class TranslationFileStore
    {
        public sealed class FileEntry
        {
            public string Key { get; set; } = "";
            public string Text { get; set; } = "";
            public string? Context { get; set; }
            public bool IsIgnored { get; set; }
        }

        public static List<FileEntry> Read(string fullPath)
        {
            var result = new List<FileEntry>();
            if (!File.Exists(fullPath)) return result;

            var json = File.ReadAllText(fullPath, Encoding.UTF8);
            var root = JObject.Parse(json);
            var node = root.ContainsKey("translations") ? root["translations"] as JObject : root;
            if (node == null) return result;

            foreach (var kv in node)
            {
                var original = kv.Key;
                var value    = kv.Value;
                if (string.IsNullOrWhiteSpace(original) || value == null) continue;

                string text;
                string? context = null;
                bool isIgnored;

                if (value.Type == JTokenType.Object)
                {
                    var obj = (JObject)value;
                    text      = obj["text"]?.ToString() ?? "";
                    context   = obj["context"]?.ToString();
                    isIgnored = obj["ignore"]?.Type == JTokenType.Boolean && (bool)obj["ignore"]!;
                    if (isIgnored) text = original;
                }
                else
                {
                    text      = value.ToString();
                    isIgnored = text == original;
                }

                result.Add(new FileEntry { Key = original, Text = text, Context = context, IsIgnored = isIgnored });
            }

            return result;
        }

        /// <summary>
        /// Cria ou atualiza uma entrada por chave. Preserva a forma existente
        /// (string simples vs. objeto estendido) — só vira objeto se precisar
        /// gravar "ignore" ou "context" numa entrada que hoje é string simples.
        /// </summary>
        public static void Upsert(string fullPath, string originalKey, string newText,
            string? context = null, bool ignore = false, string? source = null)
        {
            var root = LoadOrCreateRoot(fullPath);
            var node = GetOrCreateTranslationsNode(root);

            var storedText = ignore ? originalKey : newText;
            var existing   = node[originalKey];

            if (existing is JObject existingObj)
            {
                existingObj["text"] = storedText;
                if (context != null) existingObj["context"] = context;
                if (source  != null) existingObj["source"]  = source;
                existingObj["ignore"] = ignore;
            }
            else if (context != null || ignore || source != null)
            {
                var obj = new JObject { ["text"] = storedText };
                if (context != null) obj["context"] = context;
                if (source  != null) obj["source"]  = source;
                if (ignore) obj["ignore"] = true;
                node[originalKey] = obj;
            }
            else
            {
                node[originalKey] = storedText;
            }

            WriteAtomic(fullPath, root);
        }

        /// <summary>
        /// Regrava o arquivo inteiro a partir de um dicionário achatado (chave
        /// -> texto; chave==valor significa ignorado, convenção legada). Usado
        /// pelo fluxo Scan -> uncategorized.json, que relê tudo, muta uma
        /// chave e regrava de uma vez. Entradas em forma estendida (objeto com
        /// "context"/"tags") têm esses campos preservados — só "text"/"ignore"
        /// mudam.
        /// </summary>
        public static void WriteAll(string fullPath, IReadOnlyDictionary<string, string> flatEntries)
        {
            var root = LoadOrCreateRoot(fullPath);
            var hasWrapper = root.ContainsKey("translations");
            var oldNode = hasWrapper ? root["translations"] as JObject : root;

            var newNode = new JObject();
            foreach (var kv in flatEntries)
            {
                var key     = kv.Key;
                var newText = kv.Value;
                bool ignore = newText == key; // identidade = ignorado (convenção legada)

                if (oldNode?[key] is JObject existingObj)
                {
                    var clone = (JObject)existingObj.DeepClone();
                    clone["text"]   = ignore ? key : newText;
                    clone["ignore"] = ignore;
                    newNode[key] = clone;
                }
                else
                {
                    newNode[key] = newText;
                }
            }

            if (hasWrapper)
            {
                root["translations"] = newNode;
                WriteAtomic(fullPath, root);
            }
            else
            {
                // formato legado sem envelope $meta — preserva o estilo em vez
                // de forçar o formato novo
                var newRoot = root.ContainsKey("$meta") ? new JObject { ["$meta"] = root["$meta"] } : new JObject();
                foreach (var prop in newNode.Properties())
                    newRoot[prop.Name] = prop.Value;
                WriteAtomic(fullPath, newRoot);
            }
        }

        /// <summary>
        /// Cria o arquivo com o envelope padrão ($meta + translations vazio) se
        /// ainda não existir. Idempotente — não faz nada se já existir.
        /// </summary>
        public static void EnsureFileExists(string fullPath)
        {
            if (File.Exists(fullPath)) return;
            var root = LoadOrCreateRoot(fullPath);
            GetOrCreateTranslationsNode(root);
            WriteAtomic(fullPath, root);
        }

        public static void Remove(string fullPath, string originalKey)
        {
            if (!File.Exists(fullPath)) return;
            var root = LoadOrCreateRoot(fullPath);
            var node = root.ContainsKey("translations") ? root["translations"] as JObject : root;
            if (node == null || !node.ContainsKey(originalKey)) return;

            node.Remove(originalKey);
            WriteAtomic(fullPath, root);
        }

        private static JObject LoadOrCreateRoot(string fullPath)
        {
            if (!File.Exists(fullPath)) return new JObject();
            var json = File.ReadAllText(fullPath, Encoding.UTF8);
            return JObject.Parse(json);
        }

        private static JObject GetOrCreateTranslationsNode(JObject root)
        {
            if (root.ContainsKey("translations") && root["translations"] is JObject existing)
                return existing;

            // arquivo novo/vazio ou legado sem "translations" — se já tiver
            // qualquer propriedade além de "$meta", trata como formato legado
            // e grava na raiz, pra não reformatar o que o usuário já mantém assim
            var nonMetaKeys = root.Properties().Where(p => p.Name != "$meta").ToList();
            if (nonMetaKeys.Count > 0) return root;

            var node = new JObject();
            root["translations"] = node;
            if (!root.ContainsKey("$meta"))
                root["$meta"] = new JObject { ["schemaVersion"] = "2.0" };
            return node;
        }

        private static void WriteAtomic(string fullPath, JObject root)
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmpPath = fullPath + ".tmp";
            File.WriteAllText(tmpPath, root.ToString(Formatting.Indented), Encoding.UTF8);

            if (File.Exists(fullPath))
                File.Replace(tmpPath, fullPath, fullPath + ".bak");
            else
                File.Move(tmpPath, fullPath);
        }
    }
}
