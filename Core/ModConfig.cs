using BepInEx.Configuration;

namespace AQWMod.Localization.Core
{
    // Configurações expostas via BepInEx config (AQWTranslation.cfg), pra
    // ajustar o comportamento do mod sem recompilar.
    public static class ModConfig
    {
        // gerais
        public static ConfigEntry<bool>   Enabled           { get; private set; } = null!;
        public static ConfigEntry<string> Locale            { get; private set; } = null!;
        public static ConfigEntry<int>    CacheCapacity     { get; private set; } = null!;

        // captura
        public static ConfigEntry<bool>   CaptureEnabled    { get; private set; } = null!;
        public static ConfigEntry<int>    CaptureMinLength  { get; private set; } = null!;
        public static ConfigEntry<int>    CaptureFlushMins  { get; private set; } = null!;

        // aplica tradução durante a varredura contínua, alcançando texto
        // definido por caminho de escrita que os patches Harmony não interceptam
        public static ConfigEntry<bool>   EnforceOnScan     { get; private set; } = null!;

        // hot reload
        public static ConfigEntry<bool>   HotReloadEnabled  { get; private set; } = null!;
        public static ConfigEntry<int>    HotReloadDebounce { get; private set; } = null!;

        // debug
        public static ConfigEntry<bool>   VerboseLogging    { get; private set; } = null!;
        public static ConfigEntry<bool>   LogMisses         { get; private set; } = null!;
        public static ConfigEntry<bool>   ShowStatsOnExit   { get; private set; } = null!;

        // só afeta o botão "Auto Traduzir" da GUI — nunca roda sozinho, nunca
        // sobrescreve tradução manual sem confirmação explícita no preview
        public static ConfigEntry<bool>   AutoTranslateEnabled    { get; private set; } = null!;
        public static ConfigEntry<string> AutoTranslateSourceLang { get; private set; } = null!;
        public static ConfigEntry<string> AutoTranslateTargetLang { get; private set; } = null!;

        public static void Initialize(ConfigFile config)
        {
            Enabled = config.Bind(
                "General", "Enabled", true,
                "Ativa ou desativa o mod de tradução completamente.");

            Locale = config.Bind(
                "General", "Locale", "pt-BR",
                "Código do idioma alvo (ex: pt-BR, es-ES).");

            CacheCapacity = config.Bind(
                "Performance", "CacheCapacity", 4096,
                "Número máximo de strings em cache LRU. Aumente se o jogo tiver muitos textos únicos.");

            CaptureEnabled = config.Bind(
                "Capture", "CaptureEnabled", true,
                "Salva automaticamente strings sem tradução para o arquivo _capture.");

            CaptureMinLength = config.Bind(
                "Capture", "CaptureMinLength", 3,
                "Comprimento mínimo de uma string para ser capturada.");

            CaptureFlushMins = config.Bind(
                "Capture", "CaptureFlushMinutes", 5,
                "Intervalo em minutos para salvar strings capturadas em disco.");

            EnforceOnScan = config.Bind(
                "Capture", "EnforceOnScan", true,
                "Reaplica traduções nos textos visíveis durante a varredura contínua. " +
                "Alcança textos cujo caminho de escrita não é interceptado pelos patches " +
                "Harmony (ex.: popup 'Goto map ...'). Desative se causar efeitos colaterais.");

            HotReloadEnabled = config.Bind(
                "HotReload", "HotReloadEnabled", true,
                "Recarrega arquivos de tradução automaticamente quando modificados.");

            HotReloadDebounce = config.Bind(
                "HotReload", "DebounceMs", 500,
                "Tempo de debounce em ms para hot reload (evita múltiplos reloads rápidos).");

            VerboseLogging = config.Bind(
                "Debug", "VerboseLogging", false,
                "Loga cada tradução aplicada. Impacta performance — use apenas para debug.");

            LogMisses = config.Bind(
                "Debug", "LogMisses", false,
                "Loga strings sem tradução encontrada no console.");

            ShowStatsOnExit = config.Bind(
                "Debug", "ShowStatsOnExit", true,
                "Exibe estatísticas de cache/tradução quando o jogo fecha.");

            AutoTranslateEnabled = config.Bind(
                "AutoTranslate", "Enabled", true,
                "Habilita o botão 'Auto Traduzir' da GUI (usa uma API pública de " +
                "tradução). O botão sempre pede confirmação antes de aplicar — " +
                "desativar aqui só esconde/desabilita o recurso, nunca traduz nada sozinho.");

            AutoTranslateSourceLang = config.Bind(
                "AutoTranslate", "SourceLang", "en",
                "Idioma de origem para a tradução automática.");

            AutoTranslateTargetLang = config.Bind(
                "AutoTranslate", "TargetLang", "pt-BR",
                "Idioma de destino para a tradução automática.");
        }
    }
}
