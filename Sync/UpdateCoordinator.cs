using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    public enum UpdateStatus
    {
        UpToDate,      // já está na versão do release (ou mais nova)
        Updated,       // pacote novo aplicado
        NoRelease,     // repositório sem release publicado
        Offline,       // sem rede / timeout / GitHub fora do ar
        Incompatible,  // release exige uma versão mais nova do mod
        Failed,        // manifesto/pacote inválido, erro de disco etc.
    }

    public sealed class UpdateResult
    {
        public UpdateStatus Status { get; set; }
        public string? LocalVersion { get; set; }
        public string? RemoteVersion { get; set; }
        public SyncReport? Report { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class UpdateOptions
    {
        public string LocalRoot { get; set; } = "";
        public string ModVersion { get; set; } = "0";
        public int BackupsToKeep { get; set; } = 3;
        public SyncLimits Limits { get; set; } = new SyncLimits();
    }

    // Um ciclo completo de atualização:
    //   manifesto → compara versões → baixa → confere sha256 → extrai pra staging
    //   → SyncEngine (merge 3-way, com backup) → limpa staging.
    // Nunca lança: todo erro vira UpdateResult, o jogo segue com o que tem (offline-first).
    public static class UpdateCoordinator
    {
        public static async Task<UpdateResult> RunAsync(UpdateOptions opt, IReleaseSource source, CancellationToken ct)
        {
            var result = new UpdateResult();
            var syncDir = Path.Combine(opt.LocalRoot, SyncEngine.StateFolder);
            var staging = Path.Combine(syncDir, "staging");

            try
            {
                result.LocalVersion = ReadLocalVersion(syncDir);

                // 1) manifesto
                var json = await source.GetManifestAsync(opt.Limits.MaxManifestBytes, ct).ConfigureAwait(false);
                if (!RemoteManifest.TryParse(json, opt.Limits, out var manifest, out var error))
                    return Done(result, UpdateStatus.Failed, "manifesto inválido: " + error);
                result.RemoteVersion = manifest.PackageVersion.Raw;

                // 2) compatibilidade e versão
                if (manifest.MinModVersion != null &&
                    PackageVersion.TryParse(opt.ModVersion, out var mod) &&
                    mod.CompareTo(manifest.MinModVersion) < 0)
                    return Done(result, UpdateStatus.Incompatible,
                        $"o pacote {manifest.PackageVersion} exige o mod {manifest.MinModVersion} ou mais novo (instalado: {opt.ModVersion}).");

                if (result.LocalVersion != null &&
                    PackageVersion.TryParse(result.LocalVersion, out var local) &&
                    manifest.PackageVersion.CompareTo(local) <= 0)
                    return Done(result, UpdateStatus.UpToDate,
                        $"traduções já na versão {local} (remoto: {manifest.PackageVersion}).");

                // 3) baixa e confere
                ct.ThrowIfCancellationRequested();
                ResetDir(staging);
                var zipPath = Path.Combine(staging, "package.zip");
                await source.DownloadPackageAsync(manifest.PackageName, zipPath, manifest.PackageSizeBytes, ct).ConfigureAwait(false);
                PackageExtractor.VerifyPackageFile(zipPath, manifest);

                var pkgDir = Path.Combine(staging, "pkg");
                PackageExtractor.ExtractVerified(zipPath, manifest, pkgDir, opt.Limits);

                // 4) merge
                var backupRoot = Path.Combine(syncDir, "backup", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                var report = SyncEngine.Apply(new SyncOptions
                {
                    LocalRoot      = opt.LocalRoot,
                    RemoteRoot     = pkgDir,
                    PackageVersion = manifest.PackageVersion.Raw,
                    MaxFileBytes   = opt.Limits.MaxFileBytes,
                    BackupRoot     = backupRoot,
                });
                PruneBackups(Path.Combine(syncDir, "backup"), opt.BackupsToKeep);

                result.Report = report;
                return Done(result, UpdateStatus.Updated,
                    $"{result.LocalVersion ?? "(nenhuma)"} → {manifest.PackageVersion}: " +
                    $"{report.Changed} arquivo(s) alterado(s), {report.Conflicts.Count} conflito(s), " +
                    $"{report.OverridesWritten} versão(ões) sua(s) guardada(s) em overrides/.");
            }
            catch (ReleaseNotFoundException ex)
            {
                return Done(result, UpdateStatus.NoRelease, ex.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Done(result, UpdateStatus.Failed, "cancelado.");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException ||
                                       ex is System.Net.WebException)
            {
                // TaskCanceledException sem ct cancelado = timeout do HttpClient
                return Done(result, UpdateStatus.Offline, "sem conexão com o GitHub: " + Root(ex).Message);
            }
            catch (Exception ex)
            {
                return Done(result, UpdateStatus.Failed, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                TryDelete(staging);
            }
        }

        // ---------------------------------------------------------------- util

        private static UpdateResult Done(UpdateResult r, UpdateStatus s, string msg)
        {
            r.Status = s; r.Message = msg; return r;
        }

        private static string? ReadLocalVersion(string syncDir)
        {
            try
            {
                var p = Path.Combine(syncDir, "state.json");
                if (!File.Exists(p)) return null;
                return (string?)JObject.Parse(File.ReadAllText(p))["packageVersion"];
            }
            catch { return null; } // estado ilegível = tratar como primeira sincronização
        }

        private static void ResetDir(string dir)
        {
            TryDelete(dir);
            Directory.CreateDirectory(dir);
        }

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }

        private static void PruneBackups(string backupRoot, int keep)
        {
            try
            {
                if (!Directory.Exists(backupRoot)) return;
                var old = Directory.GetDirectories(backupRoot)
                    .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                    .Skip(Math.Max(0, keep));
                foreach (var d in old) TryDelete(d);
            }
            catch { }
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
