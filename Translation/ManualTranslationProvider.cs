using System.Threading;
using System.Threading.Tasks;

namespace AQWMod.Localization.Translation
{
    // Representa "sem tradução automática" — usado quando AutoTranslateEnabled
    // está desligado, ou como fallback seguro se nenhum provedor real puder
    // ser instanciado. Nunca chama rede, sempre falha de forma explícita.
    public sealed class ManualTranslationProvider : ITranslationProvider
    {
        public string Id => "manual";
        public bool RequiresApiKey => false;

        public Task<TranslationProviderResult> TranslateAsync(TranslationProviderRequest request, CancellationToken ct)
            => Task.FromResult(new TranslationProviderResult
            {
                Success = false,
                ErrorMessage = "Tradução automática desativada (ver Configurações).",
                ProviderId = Id,
            });
    }
}
