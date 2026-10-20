using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AQWMod.Localization.Sync
{
    public enum ModUpdateStatus
    {
        NotOffered,  // manifesto sem seção "mod" (ou inválida)
        UpToDate,    // já está na versão do release ou mais nova
        Staged,      // DLL nova gravada; vale na próxima vez que o jogo abrir
        Rejected,    // assinatura, hash ou tamanho não conferem — nada foi trocado
        Failed,      // erro de disco/rede
    }

    public sealed class ModUpdateResult
    {
        public ModUpdateStatus Status { get; set; }
        public string? NewVersion { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class ModUpdateOptions
    {
        /// <summary>Caminho da DLL carregada agora (BepInEx/plugins/AQWTranslation.dll).</summary>
        public string DllPath { get; set; } = "";
        public string Edition { get; set; } = "";
        public string PublicKeyXml { get; set; } = ModSignature.PublicKeyXml;
    }

    // Atualiza a DLL do próprio mod a partir do release:
    //   versão maior? → assinatura RSA confere? → baixa a DLL da MESMA edição →
    //   tamanho e sha256 conferem? → troca o arquivo.
    // O Mono lê a DLL inteira pra memória ao carregar, então o arquivo não
    // fica travado com o jogo aberto: a troca é feita na hora e a versão nova
    // só roda na próxima vez que o jogo abrir. A anterior fica como
    // AQWTranslation.dll.old (o BepInEx só carrega *.dll) até essa próxima
    // abertura, quando CleanupPrevious a apaga.
    public static class ModUpdater
    {
        public const string PreviousSuffix = ".old";
        private const string IncomingSuffix = ".new";

        public static async Task<ModUpdateResult> RunAsync(ModUpdateOptions opt, RemoteManifest manifest, string currentVersion,
            IReleaseSource source, string stagingDir, CancellationToken ct)
        {
            var r = new ModUpdateResult();
            var mod = manifest.Mod;
            if (mod == null)
                return Done(r, ModUpdateStatus.NotOffered, manifest.ModError != null
                    ? "seção 'mod' do manifesto ignorada: " + manifest.ModError
                    : "release sem atualização do mod.");
            r.NewVersion = mod.Version.Raw;

            if (PackageVersion.TryParse(currentVersion, out var current) && mod.Version.CompareTo(current) <= 0)
                return Done(r, ModUpdateStatus.UpToDate, $"mod já na versão {currentVersion}.");

            // a assinatura vem antes de qualquer download: sem ela, quem
            // controlasse o release poderia instalar código em todo mundo
            if (!mod.VerifySignature(opt.PublicKeyXml))
                return Done(r, ModUpdateStatus.Rejected, $"assinatura do mod {mod.Version} não confere; atualização recusada.");

            if (!mod.Editions.TryGetValue(opt.Edition, out var asset))
                return Done(r, ModUpdateStatus.Rejected, $"o release {mod.Version} não tem a edição {opt.Edition}.");

            if (string.IsNullOrEmpty(opt.DllPath) || !File.Exists(opt.DllPath))
                return Done(r, ModUpdateStatus.Failed, $"DLL atual não encontrada ('{opt.DllPath}').");

            try
            {
                Directory.CreateDirectory(stagingDir);
                var downloaded = Path.Combine(stagingDir, asset.Name);
                await source.DownloadPackageAsync(asset.Name, downloaded, asset.SizeBytes, ct).ConfigureAwait(false);

                var size = new FileInfo(downloaded).Length;
                if (size != asset.SizeBytes)
                    return Done(r, ModUpdateStatus.Rejected, $"tamanho da DLL {mod.Version} não confere ({size} != {asset.SizeBytes}).");
                if (!string.Equals(PackageExtractor.Sha256OfFile(downloaded), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Done(r, ModUpdateStatus.Rejected, $"sha256 da DLL {mod.Version} não confere.");

                Swap(opt.DllPath, downloaded);
                return Done(r, ModUpdateStatus.Staged,
                    $"mod {currentVersion} → {mod.Version} ({opt.Edition}) instalado; vale na próxima abertura do jogo.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // falha aqui não pode impedir a atualização das traduções
                // que vem logo depois (UpdateCoordinator)
                return Done(r, ModUpdateStatus.Failed, ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Troca a DLL: cópia completa ao lado (.new), atual → .old, .new →
        /// lugar da atual. Se o último passo falhar, a atual volta pro lugar.
        /// </summary>
        internal static void Swap(string dllPath, string newDll)
        {
            var incoming = dllPath + IncomingSuffix;
            var previous = dllPath + PreviousSuffix;

            File.Copy(newDll, incoming, overwrite: true);
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(dllPath, previous);
            try
            {
                File.Move(incoming, dllPath);
            }
            catch
            {
                if (!File.Exists(dllPath)) File.Move(previous, dllPath);
                throw;
            }
        }

        /// <summary>Apaga a DLL anterior deixada pela última atualização. Nunca lança.</summary>
        public static void CleanupPrevious(string dllPath)
        {
            try
            {
                foreach (var leftover in new[] { dllPath + PreviousSuffix, dllPath + IncomingSuffix })
                    if (File.Exists(leftover)) File.Delete(leftover);
            }
            catch { }
        }

        private static ModUpdateResult Done(ModUpdateResult r, ModUpdateStatus s, string msg)
        {
            r.Status = s; r.Message = msg; return r;
        }
    }
}
