using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    // Textos do aviso de atualização mostrado no jogo (Notifications/UpdatePopup).
    // Puro, sem Unity: contagem zero nunca aparece ("0 removidas" não existe),
    // e sem nada pra contar a linha inteira some.
    public static class UpdateSummary
    {
        /// <summary>Linha das traduções aplicadas nesta sessão, ou null se não houve mudança visível.</summary>
        public static string? TranslationsLine(UpdateResult r)
        {
            if (r.Status != UpdateStatus.Updated || r.Report == null) return null;

            int added   = r.Report.Files.Sum(f => f.Added);
            int updated = r.Report.Files.Sum(f => f.Updated);
            int removed = r.Report.Files.Sum(f => f.Removed);

            var parts = new List<string>();
            if (added   > 0) parts.Add(Count(added,   "nova",       "novas"));
            if (updated > 0) parts.Add(Count(updated, "atualizada", "atualizadas"));
            if (removed > 0) parts.Add(Count(removed, "removida",   "removidas"));
            if (parts.Count == 0) return null;

            return $"Traduções atualizadas para a versão {r.RemoteVersion}: {JoinPt(parts)}.";
        }

        /// <summary>Linha da DLL nova baixada nesta sessão, ou null.</summary>
        public static string? ModStagedLine(UpdateResult r) =>
            r.ModUpdate?.Status == ModUpdateStatus.Staged
                ? $"Nova versão do mod ({r.ModUpdate.NewVersion}) baixada. Ela passa a valer na próxima vez que você abrir o jogo."
                : null;

        public static string ModUpdatedLine(string previous, string current) =>
            $"O mod foi atualizado: versão {previous} → {current}.";

        // "1.234 novas" — separador de milhar com ponto, sem depender da
        // cultura do Windows do jogador
        private static string Count(int n, string singular, string plural) =>
            n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.') + " " + (n == 1 ? singular : plural);

        // "a", "a e b", "a, b e c"
        private static string JoinPt(IReadOnlyList<string> parts) =>
            parts.Count == 1 ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " e " + parts[parts.Count - 1];
    }

    // Lembra qual versão do mod rodou da última vez (_sync/mod_state.json),
    // pra avisar "o mod foi atualizado" na primeira abertura com a DLL nova.
    public static class ModVersionTracker
    {
        public const string FileName = "mod_state.json";

        /// <summary>
        /// Grava a versão atual e devolve a anterior se ela for diferente
        /// (null na primeira execução ou se nada mudou). Nunca lança.
        /// </summary>
        public static string? RecordAndGetPrevious(string syncDir, string currentVersion)
        {
            var path = Path.Combine(syncDir, FileName);
            string? previous = null;
            try
            {
                if (File.Exists(path))
                    previous = (string?)JObject.Parse(File.ReadAllText(path))["modVersion"];
            }
            catch { /* estado ilegível: trata como primeira execução */ }

            if (previous == currentVersion) return null;
            try
            {
                Directory.CreateDirectory(syncDir);
                File.WriteAllText(path, new JObject { ["modVersion"] = currentVersion }.ToString());
            }
            catch { }
            return previous;
        }
    }
}
