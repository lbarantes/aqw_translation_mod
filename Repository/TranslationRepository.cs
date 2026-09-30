using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using AQWMod.Localization.Core;
using AQWMod.Localization.Debug;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Repository
{
    // Repositório principal de traduções.
    //
    // _translations/_sourceFileMap/_keysByFileMap são ConcurrentDictionary pra
    // ReloadFile poder atualizar só as chaves do arquivo que mudou, em vez de
    // O(corpus inteiro). Na prática tudo roda no main thread da Unity, então
    // não tem contenção real — o ganho aqui é granularidade, não paralelismo.
    // LoadAll (carga inicial, antes de qualquer patch Harmony existir) ainda
    // monta tudo em Dictionary<> comum durante o merge de camadas — é mais
    // simples pra lógica de conflito — e só popula as estruturas concorrentes
    // no final, num passe só.
    //
    // Formato JSON por arquivo (docs/TRANSLATION_FORMAT.md):
    //   {
    //     "$meta": { "schemaVersion": "2.0", "category": "ui/hud", ... },
    //     "translations": {
    //       "Original text": "Texto traduzido",                         // forma simples
    //       "Attack": { "text": "Ataque", "context": "skill", ... }      // forma estendida
    //     }
    //   }
    //   ou formato legado, dicionário direto: { "Original text": "Texto traduzido" }
    //
    // Prioridade entre camadas: "overrides/**" (ou "uncategorized.json" na
    // raiz) sempre vence, sem aviso de conflito — é hierarquia por design.
    // Conflitos entre arquivos de conteúdo geram WARNING e são resolvidos por
    // ordem alfabética do caminho, nunca pela ordem que o SO devolve os arquivos.
    //
    // "npc_names.json" na raiz é a tabela de nomes de NPC do placeholder
    // {npc} (TMPTextPatch) — nunca é carregado aqui como tradução (IsNpcNamesFile).
    public sealed class TranslationRepository : ITranslationRepository
    {
        private const string OverridesFolderName = "overrides";
        // era "ingame_edits.json"; renomeado pra "uncategorized.json", mesmo papel
        // (arquivo raiz que sempre vence, ver IsOverrideLayer)
        private const string OverrideRootFileName = "uncategorized.json";
        private const string NpcNamesFileName = "npc_names.json";

        private readonly ConcurrentDictionary<TranslationKey, TranslationEntry> _translations =
            new ConcurrentDictionary<TranslationKey, TranslationEntry>();

        private readonly ConcurrentDictionary<string, string> _sourceFileMap =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _keysByFileMap =
            new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly TranslationLogger _logger;
        private string _rootPath = string.Empty;

        public int Count => _translations.Count;
        public TranslationIndex Index => new TranslationIndex(_sourceFileMap, _keysByFileMap);

        public TranslationRepository(TranslationLogger logger)
        {
            _logger = logger;
        }

        public void LoadAll(string rootPath)
        {
            _rootPath = rootPath;

            if (!Directory.Exists(rootPath))
            {
                _logger.Warning($"Pasta de traduções não encontrada: '{rootPath}'");
                _logger.Warning("Crie a pasta e adicione arquivos JSON de tradução.");
                return;
            }

            var sw = Stopwatch.StartNew();

            // ordem alfabética pelo caminho relativo, nunca a ordem que
            // Directory.GetFiles devolve (não é garantida nem estável entre SOs).
            //
            // HasUnderscoreSegment checa qualquer segmento do caminho, não só o
            // nome do arquivo — senão "_capture/dialogue.json" passava batido
            // (o nome "dialogue.json" não começa com "_", só a pasta).
            var files = Directory
                .GetFiles(rootPath, "*.json", SearchOption.AllDirectories)
                .Select(f => (Full: f, Rel: GetRelativePath(f)))
                .Where(f => !HasUnderscoreSegment(f.Rel))
                .Where(f => !IsNpcNamesFile(f.Rel))
                .OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var contentFiles  = files.Where(f => !IsOverrideLayer(f.Rel)).ToList();
            var overrideFiles = files.Where(f =>  IsOverrideLayer(f.Rel)).ToList();

            var contentDict   = new Dictionary<TranslationKey, TranslationEntry>(4096);
            var contentSource = new Dictionary<string, string>(StringComparer.Ordinal);
            var overrideDict   = new Dictionary<TranslationKey, TranslationEntry>();
            var overrideSource = new Dictionary<string, string>(StringComparer.Ordinal);
            var keysByFile = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            int loadedFiles = 0, skipped = 0;

            MergeLayer(contentFiles,  contentDict,  contentSource,  keysByFile, "conteúdo", ref loadedFiles, ref skipped);
            MergeLayer(overrideFiles, overrideDict, overrideSource, keysByFile, "override", ref loadedFiles, ref skipped);

            // override sempre vence sobre conteúdo — sem log de conflito, é a
            // hierarquia esperada, não uma colisão acidental
            var finalDict   = new Dictionary<TranslationKey, TranslationEntry>(contentDict);
            var finalSource = new Dictionary<string, string>(contentSource, StringComparer.Ordinal);
            foreach (var kv in overrideDict)
            {
                finalDict[kv.Key] = kv.Value;
                finalSource[kv.Key.ToString()] = overrideSource[kv.Key.ToString()];
            }

            // popula as estruturas concorrentes num passe só — ainda sem leitor
            // concorrente possível aqui, LoadAll roda antes de qualquer patch existir
            _translations.Clear();
            foreach (var kv in finalDict) _translations[kv.Key] = kv.Value;
            _sourceFileMap.Clear();
            foreach (var kv in finalSource) _sourceFileMap[kv.Key] = kv.Value;
            _keysByFileMap.Clear();
            foreach (var kv in keysByFile) _keysByFileMap[kv.Key] = kv.Value;

            sw.Stop();
            _logger.Info(
                $"Carregados {finalDict.Count} strings de {loadedFiles} arquivo(s) " +
                $"em {sw.ElapsedMilliseconds}ms" +
                (skipped > 0 ? $" ({skipped} arquivo(s) com erro, verifique o log)" : "."));
        }

        public void ReloadFile(string filePath)
        {
            var relPath    = GetRelativePath(filePath);
            var isOverride = IsOverrideLayer(relPath);

            List<(TranslationKey Key, TranslationEntry Entry)> entries;
            try { entries = LoadFile(filePath); }
            catch (Exception ex)
            {
                _logger.Error($"[HotReload] Falha ao carregar '{relPath}': {ex.Message}");
                return;
            }

            // atualiza só as chaves deste arquivo — O(chaves do arquivo), não
            // O(corpus inteiro). Cada write é atômica por conta do
            // ConcurrentDictionary; não precisa de lock porque só o main
            // thread mexe aqui de qualquer forma.
            var keysHere    = new List<string>(entries.Count);
            var keysHereSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, entry) in entries)
            {
                var keyStr = key.ToString();
                keysHere.Add(keyStr);
                keysHereSet.Add(keyStr);

                if (!isOverride &&
                    _sourceFileMap.TryGetValue(keyStr, out var existingSrc) &&
                    !string.Equals(existingSrc, relPath, StringComparison.OrdinalIgnoreCase) &&
                    !IsOverrideLayer(existingSrc) &&
                    _translations.TryGetValue(key, out var existingEntry) &&
                    !string.Equals(existingEntry.Text, entry.Text, StringComparison.Ordinal))
                {
                    _logger.Warning(
                        $"[HotReload][Conflito] '{key}' já definido por '{existingSrc}' com valor " +
                        $"diferente ('{existingEntry.Text}' vs '{entry.Text}' em '{relPath}') — " +
                        "mantendo a definição existente. Use 'context' para desambiguar.");
                    continue;
                }

                _translations[key]      = entry;
                _sourceFileMap[keyStr]  = relPath;
            }

            // chaves que este arquivo tinha antes e não tem mais (ex.: removidas
            // pelo botão Del) precisam sumir daqui também, senão ficam presas
            // em _translations pra sempre — o laço acima só adiciona/atualiza,
            // nunca detecta o que sumiu. Só remove se este arquivo ainda for o
            // dono da chave em memória; se outra camada já sobrescreveu, não é
            // problema desta recarga.
            if (_keysByFileMap.TryGetValue(relPath, out var previousKeys))
            {
                foreach (var oldKeyStr in previousKeys)
                {
                    if (keysHereSet.Contains(oldKeyStr)) continue;
                    if (_sourceFileMap.TryGetValue(oldKeyStr, out var owner) &&
                        string.Equals(owner, relPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _translations.TryRemove(TranslationKey.Parse(oldKeyStr), out _);
                        _sourceFileMap.TryRemove(oldKeyStr, out _);
                    }
                }
            }

            _keysByFileMap[relPath] = keysHere;

            _logger.Info($"[HotReload] '{relPath}': {entries.Count} strings atualizadas.");
        }

        public bool TryGet(string key, out string value) => TryGet(key, null, out value);

        public bool TryGet(string key, string? context, out string value)
        {
            if (context != null &&
                _translations.TryGetValue(new TranslationKey(key, context), out var withContext))
            {
                value = withContext.Text;
                return true;
            }

            if (_translations.TryGetValue(new TranslationKey(key), out var bare))
            {
                value = bare.Text;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private void MergeLayer(
            List<(string Full, string Rel)> layerFiles,
            Dictionary<TranslationKey, TranslationEntry> targetDict,
            Dictionary<string, string> targetSource,
            Dictionary<string, IReadOnlyList<string>> keysByFile,
            string layerLabel,
            ref int loadedFiles,
            ref int skipped)
        {
            foreach (var file in layerFiles)
            {
                try
                {
                    var entries  = LoadFile(file.Full);
                    var keysHere = new List<string>(entries.Count);

                    foreach (var (key, entry) in entries)
                    {
                        var keyStr = key.ToString();
                        keysHere.Add(keyStr);

                        if (targetDict.TryGetValue(key, out var existing) &&
                            !string.Equals(existing.Text, entry.Text, StringComparison.Ordinal))
                        {
                            _logger.Warning(
                                $"[Conflito de {layerLabel}] '{key}' definido de forma diferente em " +
                                $"'{targetSource[keyStr]}' ('{existing.Text}') e '{file.Rel}' " +
                                $"('{entry.Text}') — mantendo a primeira definição (ordem alfabética). " +
                                "Considere adicionar 'context' para desambiguar (docs/adr/0001-translation-identity.md).");
                            continue; // primeira definição (ordem alfabética) vence dentro da camada
                        }

                        targetDict[key]      = entry;
                        targetSource[keyStr] = file.Rel;
                    }

                    keysByFile[file.Rel] = keysHere;
                    loadedFiles++;

                    if (ModConfig.VerboseLogging?.Value == true)
                        _logger.Debug($"Carregado '{file.Rel}': {entries.Count} strings");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Erro ao carregar '{file.Full}': {ex.Message}");
                    skipped++;
                }
            }
        }

        private static bool HasUnderscoreSegment(string relativePath) =>
            relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(seg => seg.StartsWith("_", StringComparison.Ordinal));

        // só é reservado na RAIZ de translations/, mesmo lugar de onde
        // TMPTextPatch.LoadNpcNames lê. Um arquivo de conteúdo real com esse
        // nome numa subpasta carregaria normal — caso improvável, não vale
        // complicar a checagem por causa dele.
        private static bool IsNpcNamesFile(string relativePath)
        {
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length == 1 &&
                   string.Equals(parts[0], NpcNamesFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOverrideLayer(string relativePath)
        {
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length == 0) return false;

            if (string.Equals(parts[0], OverridesFolderName, StringComparison.OrdinalIgnoreCase))
                return true;

            // translations/uncategorized.json na raiz também conta como override
            if (parts.Length == 1 &&
                string.Equals(parts[0], OverrideRootFileName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Parseia um único arquivo sem tocar em estado compartilhado — puro,
        /// testável isoladamente. Suporta forma simples (string) e estendida
        /// (text/context/status/source/tags/ignore), com ou sem envelope $meta.
        /// internal porque Migration/IngameEditsMigrator reaproveita este parser.
        /// </summary>
        internal static List<(TranslationKey Key, TranslationEntry Entry)> LoadFile(string filePath)
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var root = JObject.Parse(json);

            JObject? translationsNode = root.ContainsKey("translations")
                ? root["translations"] as JObject
                : root; // Formato legado

            var result = new List<(TranslationKey, TranslationEntry)>();
            if (translationsNode == null) return result;

            foreach (var kv in translationsNode)
            {
                var original = kv.Key;
                if (string.IsNullOrWhiteSpace(original)) continue;

                var value = kv.Value;
                if (value == null) continue;

                string? text;
                string? context = null;
                string? status  = null;
                string? source  = null;
                string[]? tags  = null;
                bool isIgnored;

                if (value.Type == JTokenType.Object)
                {
                    var obj = (JObject)value;
                    text    = obj["text"]?.ToString();
                    context = obj["context"]?.ToString();
                    status  = obj["status"]?.ToString();
                    source  = obj["source"]?.ToString();

                    // forma estendida: "ignore" é sempre explícito, nunca inferido
                    // por coincidência de texto — senão {"text":"OK","ignore":false}
                    // (tradução real que por acaso é igual ao original) viraria
                    // "ignorada" por engano
                    isIgnored = obj["ignore"]?.Type == JTokenType.Boolean && (bool)obj["ignore"]!;

                    if (obj["tags"] is JArray tagsArr)
                        tags = tagsArr.Select(t => t?.ToString() ?? "")
                                      .Where(s => !string.IsNullOrWhiteSpace(s))
                                      .ToArray();
                }
                else
                {
                    text = value.ToString();

                    // forma simples/legada: a única forma de "ignorar" é o valor
                    // ser idêntico à chave (mesma convenção da GUI, IgnoreEntry).
                    // Precisa ser armazenada como identidade, não descartada —
                    // senão o MissingTranslationCollector fica recapturando essa
                    // entrada pra sempre como "faltando tradução".
                    isIgnored = text != null && text == original;
                }

                var finalText = isIgnored ? original : text;
                if (string.IsNullOrWhiteSpace(finalText)) continue; // ex.: entrada de _capture ainda pendente

                var key   = new TranslationKey(original, context);
                var entry = new TranslationEntry
                {
                    Text   = finalText!,
                    Status = status ?? (isIgnored ? "ignored" : null),
                    Source = source,
                    Tags   = tags,
                };

                result.Add((key, entry));
            }

            return result;
        }

        private string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(_rootPath)) return fullPath;
            try
            {
                // Path.GetRelativePath nao existe em net48 — implementacao manual
                var rootUri = new Uri(_rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                var fileUri = new Uri(fullPath);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
                           .Replace('/', Path.DirectorySeparatorChar);
            }
            catch { return fullPath; }
        }
    }
}
