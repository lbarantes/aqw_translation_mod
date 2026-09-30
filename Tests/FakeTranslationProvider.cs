using System;
using System.Threading;
using System.Threading.Tasks;
using AQWMod.Localization.Translation;

namespace AQWMod.Localization.Tests
{
    // Provedor falso para testar AutoTranslationOrchestrator sem bater numa
    // API de verdade (MyMemory) — determinístico, instantâneo, e conta
    // quantas vezes foi chamado (para validar cache/retry).
    internal sealed class FakeTranslationProvider : ITranslationProvider
    {
        private readonly Func<TranslationProviderRequest, TranslationProviderResult> _respond;
        private int _callCount;

        public int CallCount => _callCount;

        public string Id => "fake";
        public bool RequiresApiKey => false;

        public FakeTranslationProvider(Func<TranslationProviderRequest, TranslationProviderResult> respond)
        {
            _respond = respond;
        }

        // Provedor "eco" — devolve o texto exatamente como recebeu. Usado
        // para validar o round-trip completo (proteger tags/números antes de
        // enviar, restaurar depois) sem depender de uma tradução real.
        public static FakeTranslationProvider Echo() =>
            new FakeTranslationProvider(req => new TranslationProviderResult
            {
                Success = true,
                TranslatedText = req.SourceText,
            });

        public static FakeTranslationProvider AlwaysFails(string error = "erro simulado") =>
            new FakeTranslationProvider(_ => new TranslationProviderResult
            {
                Success = false,
                ErrorMessage = error,
            });

        public Task<TranslationProviderResult> TranslateAsync(TranslationProviderRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_respond(request));
        }
    }
}
