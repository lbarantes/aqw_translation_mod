using System.Threading;
using System.Threading.Tasks;

namespace AQWMod.Localization.Translation
{
    // Ponto de contato único com qualquer fornecedor externo de tradução
    // automática. Sem TranslateBatchAsync na interface de propósito — MyMemory
    // (única implementação hoje) só faz uma chamada por string, então lote
    // ficaria sem uso; o AutoTranslationOrchestrator é quem decide como
    // percorrer várias strings (loop com rate limit).
    public sealed class TranslationProviderRequest
    {
        public string SourceText { get; }
        public string SourceLang { get; }
        public string TargetLang { get; }

        public TranslationProviderRequest(string sourceText, string sourceLang, string targetLang)
        {
            SourceText = sourceText;
            SourceLang = sourceLang;
            TargetLang = targetLang;
        }
    }

    public sealed class TranslationProviderResult
    {
        public bool Success { get; set; }
        public string? TranslatedText { get; set; }
        public string? ErrorMessage { get; set; }
        public string ProviderId { get; set; } = "";
        public long LatencyMs { get; set; }
    }

    public interface ITranslationProvider
    {
        string Id { get; }
        bool RequiresApiKey { get; }

        Task<TranslationProviderResult> TranslateAsync(TranslationProviderRequest request, CancellationToken ct);
    }
}
