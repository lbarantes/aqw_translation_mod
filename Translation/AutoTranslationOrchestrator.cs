using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Interceptors;
using AQWMod.Localization.Utils;

namespace AQWMod.Localization.Translation
{
    // Tudo que NÃO é específico de um provedor: rate limit, retry, cache de
    // tentativas, cancelamento, e a validação de placeholders que decide se
    // uma tradução é segura pra propor. Reaproveita RichTextUtils pra
    // proteger tags antes de enviar e reinjetá-las depois — mesma lógica do
    // RichTextStage do pipeline de runtime.
    //
    // Nunca aplica uma tradução sozinho, só produz propostas (TranslationOutcome)
    // — quem decide gravar é o chamador (GUI), depois de preview com
    // confirmação explícita.
    public sealed class AutoTranslationOrchestrator
    {
        private readonly ITranslationProvider _provider;

        // cache desta sessão só, pra não reconsultar a mesma string duas
        // vezes; não precisa persistir entre reinícios do jogo
        private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

        private DateTime _lastCallUtc = DateTime.MinValue;
        private static readonly TimeSpan MinIntervalBetweenCalls = TimeSpan.FromMilliseconds(400);

        public string ProviderId => _provider.Id;

        public AutoTranslationOrchestrator(ITranslationProvider provider) => _provider = provider;

        public sealed class Candidate
        {
            public string OriginalKey = "";      // chave de lookup (texto normalizado)
            public string TextToSend  = "";       // texto já sem tags rich text
            public List<string> Tags  = new();    // tags extraídas, para reinjeção
            public int PlaceholderCount;          // quantos "{" existiam no texto enviado

            // números protegidos como {n1}/{n2}/... antes de enviar, só quando o
            // texto ainda não usava a convenção {token} (ver BuildCandidate)
            public string[] CapturedNumbers = Array.Empty<string>();
            // true = os tokens {n...} foram inventados aqui (a chave original não
            // tinha) — a tradução final precisa voltar a ter os números literais,
            // senão sobra um "{n1}" órfão que nada resolve em runtime. false = a
            // chave já usava {n...} de verdade, a tradução final deve manter os tokens.
            public bool RestoreNumbersOnOutput;
        }

        public sealed class TranslationOutcome
        {
            public string  OriginalKey = "";
            public bool    Success;
            public string? TranslatedText;     // já com tags reinjetadas
            public bool    PlaceholderMismatch; // true = suspeito, não deve ser aplicável sem revisão
            public string? Error;
        }

        /// <summary>
        /// Prepara um candidato a partir do texto bruto: extrai tags rich text
        /// (protegendo do provedor), protege números soltos quando cabível, e
        /// conta placeholders "{...}" pra validação pós-tradução.
        /// </summary>
        public static Candidate BuildCandidate(string originalKey, string rawText)
        {
            var textToSend = rawText;
            var tags       = new List<string>();

            if (RichTextUtils.HasRichText(textToSend))
                textToSend = RichTextUtils.StripAndCapture(textToSend, out tags);

            // protege números (ex.: "1/30" -> "{n1}/{n2}") antes de enviar — uma
            // API de tradução genérica pode reformular/reordenar números soltos e
            // corromper o formato. Só age quando o texto ainda não usa {token};
            // se já usa (caso comum vindo do Scan, tipo "{n1}/{n2} Frogzards
            // Defeated"), reprocessar re-numeraria o "1" que já é parte do nome
            // do token — errado.
            var capturedNumbers = Array.Empty<string>();
            var restoreOnOutput = false;
            if (!textToSend.Contains('{'))
            {
                var withNumbers = TMPTextPatch.NormalizeNumbers(textToSend, out var nums);
                if (nums.Length > 0)
                {
                    textToSend      = withNumbers;
                    capturedNumbers = nums;
                    restoreOnOutput = true;
                }
            }

            return new Candidate
            {
                OriginalKey          = originalKey,
                TextToSend           = textToSend,
                Tags                 = tags,
                CapturedNumbers      = capturedNumbers,
                RestoreNumbersOnOutput = restoreOnOutput,
                PlaceholderCount     = CountPlaceholders(textToSend),
            };
        }

        public async Task<List<TranslationOutcome>> TranslateBatchAsync(
            IReadOnlyList<Candidate> candidates,
            string sourceLang,
            string targetLang,
            CancellationToken ct,
            Action<int, int>? onProgress = null)
        {
            var results = new List<TranslationOutcome>(candidates.Count);

            for (int i = 0; i < candidates.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await TranslateOneWithRetry(candidates[i], sourceLang, targetLang, ct)
                    .ConfigureAwait(false));
                onProgress?.Invoke(i + 1, candidates.Count);
            }

            return results;
        }

        private async Task<TranslationOutcome> TranslateOneWithRetry(
            Candidate c, string sourceLang, string targetLang, CancellationToken ct)
        {
            if (_cache.TryGetValue(c.TextToSend, out var cachedText))
                return Finish(c, cachedText);

            var result = await CallProvider(c, sourceLang, targetLang, ct).ConfigureAwait(false);

            if (!result.Success)
            {
                // uma retentativa com atraso — falha transiente (timeout, hiccup
                // do provedor gratuito) não desiste de cara, mas também não insiste à toa
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* cancelado durante o delay, segue e falha abaixo */ }

                result = await CallProvider(c, sourceLang, targetLang, ct).ConfigureAwait(false);
            }

            if (!result.Success || string.IsNullOrWhiteSpace(result.TranslatedText))
                return new TranslationOutcome
                {
                    OriginalKey = c.OriginalKey,
                    Success     = false,
                    Error       = result.ErrorMessage ?? "falha desconhecida",
                };

            _cache[c.TextToSend] = result.TranslatedText!;
            return Finish(c, result.TranslatedText!);
        }

        private async Task<TranslationProviderResult> CallProvider(
            Candidate c, string sourceLang, string targetLang, CancellationToken ct)
        {
            await RespectRateLimit(ct).ConfigureAwait(false);
            return await _provider.TranslateAsync(
                new TranslationProviderRequest(c.TextToSend, sourceLang, targetLang), ct).ConfigureAwait(false);
        }

        private TranslationOutcome Finish(Candidate c, string translatedProtected)
        {
            int countAfter = CountPlaceholders(translatedProtected);
            bool mismatch  = countAfter != c.PlaceholderCount;

            // só restaura números literais quando os tokens foram inventados
            // aqui — se a chave já usava {n...} de verdade, o valor final mantém
            // os tokens (o runtime resolve via TMPTextPatch.RestoreNumbers na exibição)
            var withNumbers = c.RestoreNumbersOnOutput
                ? TMPTextPatch.RestoreNumbers(translatedProtected, c.CapturedNumbers)
                : translatedProtected;

            var final = RichTextUtils.Reinject(withNumbers, c.Tags);
            return new TranslationOutcome
            {
                OriginalKey         = c.OriginalKey,
                Success             = true,
                TranslatedText      = final,
                PlaceholderMismatch = mismatch,
            };
        }

        private static int CountPlaceholders(string s)
        {
            int n = 0;
            foreach (var ch in s)
                if (ch == '{') n++;
            return n;
        }

        private async Task RespectRateLimit(CancellationToken ct)
        {
            var elapsed = DateTime.UtcNow - _lastCallUtc;
            if (elapsed < MinIntervalBetweenCalls)
                await Task.Delay(MinIntervalBetweenCalls - elapsed, ct).ConfigureAwait(false);
            _lastCallUtc = DateTime.UtcNow;
        }
    }
}
