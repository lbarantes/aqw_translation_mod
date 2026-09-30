using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Translation
{
    // API pública gratuita (mymemory.translated.net), sem chave — escolhida
    // como primeiro provedor por não exigir cadastro. Limite de uso diário
    // pra uso anônimo, qualidade de rascunho (sempre precisa revisão humana),
    // sem lote real (uma requisição HTTP por string). Trocar de provedor no
    // futuro é só implementar uma nova ITranslationProvider.
    public sealed class MyMemoryTranslationProvider : ITranslationProvider
    {
        public string Id => "mymemory";
        public bool RequiresApiKey => false;

        // estático e compartilhado — HttpClient é feito pra ser reutilizado
        private static readonly HttpClient s_http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public async Task<TranslationProviderResult> TranslateAsync(TranslationProviderRequest request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var langPair = $"{request.SourceLang}|{request.TargetLang}";
                var url = "https://api.mymemory.translated.net/get" +
                          $"?q={Uri.EscapeDataString(request.SourceText)}" +
                          $"&langpair={Uri.EscapeDataString(langPair)}";

                using var response = await s_http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return Fail($"HTTP {(int)response.StatusCode}", sw);

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);

                var status = json["responseStatus"]?.ToObject<int>() ?? 0;
                var text   = json["responseData"]?["translatedText"]?.ToString();

                if (status != 200 || string.IsNullOrWhiteSpace(text))
                    return Fail($"Resposta inválida do provedor (status {status}).", sw);

                // MyMemory não tem campo de erro dedicado pra "limite atingido" —
                // devolve isso dentro do próprio texto traduzido
                if (text.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Fail("Limite diário do provedor gratuito atingido — tente novamente mais tarde.", sw);

                return new TranslationProviderResult
                {
                    Success = true,
                    TranslatedText = WebUtility.HtmlDecode(text),
                    ProviderId = Id,
                    LatencyMs = sw.ElapsedMilliseconds,
                };
            }
            catch (OperationCanceledException)
            {
                return Fail("Cancelado.", sw);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message, sw);
            }
        }

        private TranslationProviderResult Fail(string message, Stopwatch sw) => new TranslationProviderResult
        {
            Success = false,
            ErrorMessage = message,
            ProviderId = Id,
            LatencyMs = sw.ElapsedMilliseconds,
        };
    }
}
