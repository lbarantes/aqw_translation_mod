using System;
using System.Threading;
using System.Threading.Tasks;

namespace AQWMod.Localization.Sync
{
    /// <summary>Ainda não existe release publicado (HTTP 404).</summary>
    public sealed class ReleaseNotFoundException : Exception
    {
        public ReleaseNotFoundException(string message) : base(message) { }
    }

    // De onde vem o pacote. A implementação real fala HTTPS com o GitHub; nos
    // testes entra uma falsa — assim a lógica de atualização se testa sem rede.
    public interface IReleaseSource
    {
        /// <summary>Texto do manifest.json do release mais recente.</summary>
        Task<string> GetManifestAsync(long maxBytes, CancellationToken ct);

        /// <summary>Baixa o asset do release mais recente para um arquivo local, abortando acima de maxBytes.</summary>
        Task DownloadPackageAsync(string packageName, string destFile, long maxBytes, CancellationToken ct);
    }
}
