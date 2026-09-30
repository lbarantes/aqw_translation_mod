namespace AQWMod.Localization.Core
{
    // Interfaces principais do sistema de localização — separam contrato de
    // implementação pra facilitar teste e troca de implementação.

    public interface ITranslationRepository
    {
        int Count { get; }

        /// <summary>Lookup pela chave sem contexto (comportamento original).</summary>
        bool TryGet(string key, out string value);

        /// <summary>Tenta "key@@context" primeiro (se context != null), depois cai pra "key" pura.</summary>
        bool TryGet(string key, string? context, out string value);

        void LoadAll(string rootPath);
        void ReloadFile(string filePath);

        /// <summary>Proveniência arquivo/chave da última carga.</summary>
        TranslationIndex Index { get; }
    }

    public interface ILocalizationPipeline
    {
        PipelineResult Process(TranslationContext context);
    }

    public interface IMissingTranslationCollector
    {
        void Capture(string text, TranslationContext? context);
        void FlushToDisk();
        int PendingCount { get; }
    }

    public interface IPipelineStage
    {
        /// <summary>Processa o contexto. Retorna false para interromper o pipeline.</summary>
        bool Process(PipelineContext context);
    }
}
