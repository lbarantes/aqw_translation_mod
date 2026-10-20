using System;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Core;
using AQWMod.Localization.Notifications;
using AQWMod.Localization.Repository;

namespace AQWMod.Localization.Sync
{
    // Dispara UMA checagem de atualização por sessão, fora da main thread, sem
    // nunca bloquear nem derrubar o jogo. Os arquivos gravados pelo SyncEngine
    // são pegos pelo hot reload normal (HotReloadWatcher) — não há chamada
    // direta ao repositório daqui.
    internal static class UpdateRunner
    {
        private static int _started;

        public static void StartOnce(string translationsPath, string modVersion, string dllPath, string edition)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            if (!ModConfig.UpdatesEnabled.Value)
            {
                Plugin.Log.LogInfo("[AQWTranslation][Update] Atualização automática desativada na config.");
                return;
            }

            var repo = ModConfig.UpdatesRepository.Value;
            Task.Run(async () =>
            {
                try
                {
                    using var source = new HttpReleaseSource(repo, "AQWTranslation/" + modVersion);
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

                    var options = new UpdateOptions { LocalRoot = translationsPath, ModVersion = modVersion };
                    if (ModConfig.UpdatesModAutoUpdate.Value)
                        options.Mod = new ModUpdateOptions { DllPath = dllPath, Edition = edition };

                    var result = await UpdateCoordinator.RunAsync(options, source, cts.Token).ConfigureAwait(false);

                    Log(result);
                    if (ModConfig.UpdatesShowPopup.Value)
                    {
                        var translations = UpdateSummary.TranslationsLine(result);
                        var mod = UpdateSummary.ModStagedLine(result);
                        UnityMainThreadDispatcher.Enqueue(() =>
                        {
                            UpdatePopup.Show(translations);
                            UpdatePopup.Show(mod);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[AQWTranslation][Update] Falha inesperada: {ex.Message}");
                }
            });
        }

        private static void Log(UpdateResult r)
        {
            var line = $"[AQWTranslation][Update] {r.Status}: {r.Message}";
            if (r.Status == UpdateStatus.Failed || r.Status == UpdateStatus.Incompatible)
                Plugin.Log.LogWarning(line);
            else
                Plugin.Log.LogInfo(line);

            if (r.ModUpdate != null && r.ModUpdate.Status != ModUpdateStatus.NotOffered)
            {
                var modLine = $"[AQWTranslation][Update] Mod {r.ModUpdate.Status}: {r.ModUpdate.Message}";
                if (r.ModUpdate.Status == ModUpdateStatus.Rejected || r.ModUpdate.Status == ModUpdateStatus.Failed)
                    Plugin.Log.LogWarning(modLine);
                else
                    Plugin.Log.LogInfo(modLine);
            }

            if (r.Report == null) return;
            foreach (var w in r.Report.Warnings)
                Plugin.Log.LogWarning($"[AQWTranslation][Update] {w}");
            foreach (var c in r.Report.Conflicts)
                Plugin.Log.LogInfo($"[AQWTranslation][Update] conflito em {c.File} → '{c.Key}' (sua versão ficou em overrides/)");
        }
    }
}
