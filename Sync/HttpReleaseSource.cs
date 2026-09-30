using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AQWMod.Localization.Sync
{
    // Baixa o release mais recente de um repositório GitHub pelas URLs estáveis
    //   https://github.com/{dono}/{repo}/releases/latest/download/{arquivo}
    // (redirecionam pro asset do release mais novo). Não usa a API REST, então
    // não há limite de 60 req/h nem JSON de release pra interpretar.
    //
    // HTTPS obrigatório: a URL final, depois dos redirecionamentos, também
    // precisa ser https. Nunca segue para http://.
    public sealed class HttpReleaseSource : IReleaseSource, IDisposable
    {
        public const string ManifestAssetName = "manifest.json";

        private static readonly Regex RepoRe = new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
        private static readonly Regex AssetRe = new Regex(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public HttpReleaseSource(string repository, string userAgent, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
        {
            if (!RepoRe.IsMatch(repository ?? "") || repository!.Split('/').Any(s => s.Trim('.').Length == 0))
                throw new ArgumentException("Repositório deve ser 'dono/repo'.", nameof(repository));

            _baseUrl = $"https://github.com/{repository}/releases/latest/download/";
            _http = handler != null ? new HttpClient(handler) : new HttpClient();
            _http.Timeout = timeout ?? TimeSpan.FromSeconds(60);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        public async Task<string> GetManifestAsync(long maxBytes, CancellationToken ct)
        {
            using var response = await SendAsync(ManifestAssetName, ct).ConfigureAwait(false);
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var ms = new MemoryStream();
            await CopyBoundedAsync(stream, ms, maxBytes, ct).ConfigureAwait(false);
            return new UTF8Encoding(false).GetString(ms.ToArray());
        }

        public async Task DownloadPackageAsync(string packageName, string destFile, long maxBytes, CancellationToken ct)
        {
            using var response = await SendAsync(packageName, ct).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            try
            {
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var fs = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await CopyBoundedAsync(stream, fs, maxBytes, ct).ConfigureAwait(false);
            }
            catch
            {
                try { File.Delete(destFile); } catch { }
                throw;
            }
        }

        private async Task<HttpResponseMessage> SendAsync(string assetName, CancellationToken ct)
        {
            if (!AssetRe.IsMatch(assetName)) throw new ArgumentException("Nome de asset inválido.", nameof(assetName));

            var response = await _http.GetAsync(_baseUrl + assetName, HttpCompletionOption.ResponseHeadersRead, ct)
                                      .ConfigureAwait(false);
            try
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new ReleaseNotFoundException($"'{assetName}' não encontrado (nenhum release publicado?).");
                response.EnsureSuccessStatusCode();

                var finalUri = response.RequestMessage?.RequestUri;
                if (finalUri != null && finalUri.Scheme != Uri.UriSchemeHttps)
                    throw new HttpRequestException("Redirecionamento para conexão não-HTTPS recusado.");

                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private static async Task CopyBoundedAsync(Stream source, Stream dest, long maxBytes, CancellationToken ct)
        {
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                total += n;
                if (total > maxBytes) throw new InvalidDataException($"Download maior que o limite ({maxBytes} bytes).");
                await dest.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
