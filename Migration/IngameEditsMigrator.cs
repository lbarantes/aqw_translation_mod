using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AQWMod.Localization.Core;
using AQWMod.Localization.Debug;
using AQWMod.Localization.Repository;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Migration
{
    // Converte translations/ingame_edits.json (legado, dicionário flat) pra
    // translations/overrides/ingame_edits.json (schema v2.0, camada de
    // override). Não é chamada automaticamente em lugar nenhum — só a GUI,
    // via um botão explícito, invoca Run(); nunca mexe nos dados do usuário
    // sem pedido direto.
    //
    // Garantias: nunca apaga o ingame_edits.json original (renomeia pra
    // .migrated no final); sempre faz backup antes de tocar em qualquer
    // coisa; escrita atômica; valida a contagem de chaves relendo o arquivo
    // gravado antes de considerar sucesso; conflito com conteúdo já curado
    // vai pro _migration_report.json pra revisão humana, mas o valor do
    // ingame_edits.json nunca é perdido, mesmo em conflito.
    public static class IngameEditsMigrator
    {
        public sealed class MigrationResult
        {
            public bool Success;
            public int MigratedCount;
            public int IgnoredCount;
            public int ConflictCount;
            public string? Error;
        }

        public static MigrationResult Run(string translationsRootPath, TranslationLogger logger)
        {
            var legacyPath = Path.Combine(translationsRootPath, "ingame_edits.json");
            if (!File.Exists(legacyPath))
                return Fail("translations/ingame_edits.json não encontrado — nada para migrar.");

            // backup primeiro
            Dictionary<string, string> legacy;
            try
            {
                var backupDir  = Path.Combine(translationsRootPath, "_migration_backup");
                Directory.CreateDirectory(backupDir);
                var backupPath = Path.Combine(backupDir, $"ingame_edits.{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                File.Copy(legacyPath, backupPath, overwrite: false);
                logger.Info($"[Migração] Backup salvo em '{backupPath}'.");

                legacy = ReadFlatDict(legacyPath);
            }
            catch (Exception ex)
            {
                return Fail($"Falha no backup/leitura de ingame_edits.json: {ex.Message}. Nada foi alterado.");
            }

            if (legacy.Count == 0)
                return Fail("ingame_edits.json está vazio — nada para migrar.");

            // converte e detecta conflito com a camada de conteúdo
            var contentSnapshot = BuildContentLayerSnapshot(translationsRootPath, legacyPath);

            var translationsObj = new JObject();
            var conflicts        = new JArray();
            int migrated = 0, ignored = 0, conflictCount = 0;

            foreach (var kv in legacy)
            {
                var original = kv.Key;
                var saved    = kv.Value;
                bool isIgnored = saved == original;

                if (!isIgnored &&
                    contentSnapshot.TryGetValue(original, out var existingElsewhere) &&
                    existingElsewhere != saved)
                {
                    conflicts.Add(new JObject
                    {
                        ["key"]                 = original,
                        ["ingame_edits_value"]  = saved,
                        ["content_layer_value"] = existingElsewhere,
                    });
                    conflictCount++;
                    // migra mesmo assim — nunca perde dado do usuário, o
                    // conflito fica registrado pra revisão humana
                }

                translationsObj[original] = isIgnored
                    ? (JToken)new JObject { ["ignore"] = true }
                    : (JToken)saved;

                if (isIgnored) ignored++;
                migrated++;
            }

            // escreve o novo arquivo em overrides/ (atômico)
            string newPath;
            try
            {
                var overridesDir = Path.Combine(translationsRootPath, "overrides");
                Directory.CreateDirectory(overridesDir);
                newPath = Path.Combine(overridesDir, "ingame_edits.json");

                var outRoot = new JObject
                {
                    ["$meta"] = new JObject
                    {
                        ["schemaVersion"]  = "2.0",
                        ["locale"]         = "pt-BR",
                        ["sourceLocale"]   = "en-US",
                        ["category"]       = "overrides",
                        ["description"]    = "Migrado automaticamente de translations/ingame_edits.json",
                        ["updatedAt"]      = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    },
                    ["translations"] = translationsObj,
                };

                var tmpPath = newPath + ".tmp";
                File.WriteAllText(tmpPath, outRoot.ToString(Formatting.Indented), Encoding.UTF8);

                // relê e confere a contagem antes de aceitar como sucesso
                var rereadCount = CountEntriesInExtendedFile(tmpPath);
                if (rereadCount != legacy.Count)
                {
                    File.Delete(tmpPath);
                    return Fail($"Validação falhou: esperado {legacy.Count} chaves, encontrado {rereadCount} " +
                                "no arquivo recém-gravado. Migração abortada — nada além do backup foi alterado.");
                }

                if (File.Exists(newPath))
                    File.Replace(tmpPath, newPath, newPath + ".bak");
                else
                    File.Move(tmpPath, newPath);
            }
            catch (Exception ex)
            {
                return Fail($"Falha ao escrever overrides/ingame_edits.json: {ex.Message}. " +
                            "O ingame_edits.json original NÃO foi tocado.");
            }

            // relatório de conflitos, se houver
            if (conflicts.Count > 0)
            {
                try
                {
                    var reportPath = Path.Combine(translationsRootPath, "_migration_report.json");
                    File.WriteAllText(reportPath,
                        new JObject { ["conflicts"] = conflicts }.ToString(Formatting.Indented),
                        Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    logger.Warning($"[Migração] Falha ao gravar _migration_report.json: {ex.Message} " +
                                    "(migração em si foi concluída normalmente).");
                }
            }

            // desativa o original — nunca apaga
            try
            {
                var migratedMarkerPath = legacyPath + ".migrated";
                if (File.Exists(migratedMarkerPath)) File.Delete(migratedMarkerPath);
                File.Move(legacyPath, migratedMarkerPath);
            }
            catch (Exception ex)
            {
                logger.Warning($"[Migração] Novo arquivo gravado com sucesso, mas não foi possível renomear " +
                                $"o ingame_edits.json original: {ex.Message}. Renomeie manualmente para evitar " +
                                "que ele seja carregado em duplicidade.");
            }

            logger.Info($"[Migração] {migrated} entrada(s) migrada(s) ({ignored} ignorada(s)) para " +
                        $"'overrides/ingame_edits.json'." +
                        (conflictCount > 0
                            ? $" {conflictCount} conflito(s) com conteúdo curado — ver _migration_report.json."
                            : " Sem conflitos com conteúdo curado."));

            return new MigrationResult
            {
                Success = true,
                MigratedCount = migrated,
                IgnoredCount = ignored,
                ConflictCount = conflictCount,
            };
        }

        private static MigrationResult Fail(string message) =>
            new MigrationResult { Success = false, Error = message };

        // snapshot da camada de conteúdo (tudo exceto overrides/ e o próprio
        // ingame_edits.json), só pra detectar conflito — reaproveita o parser
        // do TranslationRepository em vez de duplicar lógica de JSON
        private static Dictionary<string, string> BuildContentLayerSnapshot(
            string translationsRootPath, string excludeFullPath)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(translationsRootPath)) return result;

            var files = Directory.GetFiles(translationsRootPath, "*.json", SearchOption.AllDirectories)
                .Where(f => !string.Equals(f, excludeFullPath, StringComparison.OrdinalIgnoreCase))
                .Where(f => !HasUnderscoreSegment(f, translationsRootPath))
                .Where(f => !IsUnderOverrides(f, translationsRootPath))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    foreach (var (key, entry) in TranslationRepository.LoadFile(file))
                    {
                        if (key.Context != null) continue; // conflito só é comparável para chaves "bare"
                        if (!result.ContainsKey(key.Text)) result[key.Text] = entry.Text;
                    }
                }
                catch { /* melhor esforço — um arquivo de conteúdo corrompido não deve travar a migração */ }
            }

            return result;
        }

        private static bool IsUnderOverrides(string fullPath, string rootPath)
        {
            var rel = GetRelativePath(fullPath, rootPath);
            return rel != null && rel.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUnderscoreSegment(string fullPath, string rootPath)
        {
            var rel = GetRelativePath(fullPath, rootPath);
            if (rel == null) return false;
            return rel.Split('/').Any(seg => seg.StartsWith("_", StringComparison.Ordinal));
        }

        private static string? GetRelativePath(string fullPath, string rootPath)
        {
            try
            {
                var rootUri = new Uri(rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                var fileUri = new Uri(fullPath);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString());
            }
            catch { return null; }
        }

        private static Dictionary<string, string> ReadFlatDict(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            using var sr     = new StreamReader(path, Encoding.UTF8);
            using var reader = new JsonTextReader(sr);
            string? key = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName)
                    key = reader.Value?.ToString();
                else if (reader.TokenType == JsonToken.String && key != null)
                {
                    dict[key] = reader.Value?.ToString() ?? "";
                    key = null;
                }
            }
            return dict;
        }

        private static int CountEntriesInExtendedFile(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var root = JObject.Parse(json);
            var node = root["translations"] as JObject;
            return node?.Count ?? 0;
        }
    }
}
