using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AQWMod.Localization.Core;
using AQWMod.Localization.Debug;
using AQWMod.Localization.Utils;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Capture
{
    // Captura automática de strings sem tradução. Recebe o que passou pelo
    // pipeline sem hit, filtra ruído (números, símbolos, texto trivial, coisa
    // que já tá em PT-BR), acumula num ConcurrentDictionary e periodicamente
    // salva em translations/_capture/<categoria>.json com valor vazio, pra
    // alguém preencher depois. Faz merge com o arquivo existente — nunca apaga
    // tradução já feita.
    public sealed class MissingTranslationCollector : IMissingTranslationCollector
    {
        private readonly ConcurrentDictionary<string, CaptureEntry> _captured =
            new ConcurrentDictionary<string, CaptureEntry>(StringComparer.Ordinal);

        private readonly string          _capturePath;
        private readonly TranslationLogger _logger;
        private DateTime                 _lastFlush = DateTime.MinValue;

        private readonly int _minLength;
        private          TimeSpan _flushInterval;

        private struct CaptureEntry
        {
            public string OriginalText;
            public string Category;
            public int    Count;
            public DateTime FirstSeen;
            public DateTime LastSeen;
        }

        public int PendingCount => _captured.Count;

        public MissingTranslationCollector(string translationsRootPath, TranslationLogger logger)
        {
            _capturePath    = Path.Combine(translationsRootPath, "_capture");
            _logger         = logger;
            _minLength      = ModConfig.CaptureMinLength?.Value ?? 3;
            _flushInterval  = TimeSpan.FromMinutes(ModConfig.CaptureFlushMins?.Value ?? 5);

            Directory.CreateDirectory(_capturePath);
        }

        public void Capture(string text, TranslationContext? context)
        {
            if (!ShouldCapture(text)) return;

            var category = InferCategory(context?.ComponentPath ?? string.Empty);
            var now      = DateTime.UtcNow;

            _captured.AddOrUpdate(
                text,
                _ => new CaptureEntry
                {
                    OriginalText = text,
                    Category     = category,
                    Count        = 1,
                    FirstSeen    = now,
                    LastSeen     = now
                },
                (_, existing) =>
                {
                    existing.Count++;
                    existing.LastSeen = now;
                    return existing;
                }
            );

            if ((now - _lastFlush) > _flushInterval)
                FlushToDisk();
        }

        public void FlushToDisk()
        {
            _lastFlush = DateTime.UtcNow;

            if (_captured.IsEmpty) return;

            var byCategory = _captured.Values
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

            int totalWritten = 0;
            foreach (var kvp in byCategory)
            {
                try
                {
                    totalWritten += WriteCategory(kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    _logger.Error("[Capture] Erro ao salvar categoria '" + kvp.Key + "': " + ex.Message);
                }
            }

            if (totalWritten > 0)
                _logger.Info($"[Capture] {totalWritten} nova(s) string(s) salvas em _capture/");
        }

        private int WriteCategory(string category, List<CaptureEntry> entries)
        {
            var filePath = Path.Combine(_capturePath, $"{category}.json");

            // carrega o que já existe pra fazer merge, sem apagar tradução feita
            var existing = LoadExistingCapture(filePath);
            int added    = 0;

            foreach (var entry in entries.OrderByDescending(e => e.Count))
            {
                if (!existing.ContainsKey(entry.OriginalText))
                {
                    existing[entry.OriginalText] = string.Empty; // vazio = precisa tradução
                    added++;
                }
            }

            if (added == 0) return 0;

            // serialização manual porque a versão do Newtonsoft embutida no jogo
            // pode não ter Formatting.Indented
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$meta\": {");
            sb.AppendLine("    \"generated\": " + JsonString(DateTime.UtcNow.ToString("O")) + ",");
            sb.AppendLine("    \"category\": " + JsonString("_capture/" + category) + ",");
            sb.AppendLine("    \"total_count\": " + existing.Count + ",");
            sb.AppendLine("    \"note\": \"Preencha os valores em branco com a traducao PT-BR.\"");
            sb.AppendLine("  },");
            sb.AppendLine("  \"translations\": {");

            // preenchidos primeiro, vazios depois
            var ordered = existing
                .OrderBy(kv => string.IsNullOrEmpty(kv.Value) ? 1 : 0)
                .ThenBy(kv => kv.Key)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var comma = i < ordered.Count - 1 ? "," : "";
                sb.AppendLine("    " + JsonString(ordered[i].Key) + ": " + JsonString(ordered[i].Value) + comma);
            }

            sb.AppendLine("  }");
            sb.Append("}");

            File.WriteAllText(filePath, sb.ToString(), System.Text.Encoding.UTF8);

            return added;
        }

        private static Dictionary<string, string> LoadExistingCapture(string filePath)
        {
            if (!File.Exists(filePath))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var root = JObject.Parse(json);

                var trans = root["translations"] as JObject ?? root;
                return trans.Properties()
                    .ToDictionary(p => p.Name,
                                  p => p.Value.Type == JTokenType.String ? (string)p.Value! : "",
                                  StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static string JsonString(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t") + "\"";
        }

        private bool ShouldCapture(string text)
        {
            if (string.IsNullOrWhiteSpace(text))  return false;
            if (text.Length < _minLength)          return false;
            if (IsNumericOrSymbolOnly(text))       return false;
            if (IsUrl(text))                       return false;
            if (SeemsPortuguese(text))             return false;
            if (IsSingleWord(text))                return false; // palavra solta raramente precisa de contexto
            return true;
        }

        private static bool IsNumericOrSymbolOnly(string text)
        {
            foreach (char c in text)
                if (char.IsLetter(c)) return false;
            return true;
        }

        private static bool IsUrl(string text) =>
            text.StartsWith("http") || text.StartsWith("www.");

        private static bool SeemsPortuguese(string text) =>
            text.Contains("ção") || text.Contains("ão") || text.Contains("ã") ||
            text.Contains("ç")   || text.Contains("â")  || text.Contains("ê")  ||
            text.Contains("î")   || text.Contains("ô");

        private static bool IsSingleWord(string text) =>
            !text.Contains(' ') && !text.Contains('\n') && text.Length < 20;

        private static string InferCategory(string componentPath)
        {
            if (string.IsNullOrEmpty(componentPath)) return "unknown";

            var path = componentPath.ToLowerInvariant();

            if (path.Contains("shop"))      return "shops";
            if (path.Contains("quest"))     return "quests";
            if (path.Contains("dialogue") ||
                path.Contains("dialog"))    return "dialogue";
            if (path.Contains("hud") ||
                path.Contains("ui") ||
                path.Contains("menu"))      return "ui";
            if (path.Contains("item") ||
                path.Contains("inventory")) return "items";
            if (path.Contains("npc"))       return "npcs";
            if (path.Contains("tooltip"))   return "tooltips";
            if (path.Contains("skill") ||
                path.Contains("class"))     return "skills";

            return "unknown";
        }
    }
}
