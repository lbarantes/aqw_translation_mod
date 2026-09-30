using BepInEx.Logging;

namespace AQWMod.Localization.Debug
{
    // Wrapper sobre o ManualLogSource do BepInEx: prefixo "[AQWTranslation]"
    // consistente em tudo, e uma versão estática (StaticError/StaticWarning/
    // StaticDebug) pra usar dentro de patches Harmony, que são estáticos.
    // Debug()/StaticDebug() só logam quando VerboseLogging está ligado.
    public sealed class TranslationLogger
    {
        private readonly ManualLogSource _source;

        // acesso estático pra patches Harmony (classes estáticas)
        private static ManualLogSource? _staticSource;

        public TranslationLogger()
        {
            _source = Plugin.Log;
            _staticSource = _source;
        }

        public void Debug(string msg)
        {
            if (Core.ModConfig.VerboseLogging?.Value == true)
                _source.LogDebug($"[AQWTranslation] {msg}");
        }

        public void Info(string msg)    => _source.LogInfo($"[AQWTranslation] {msg}");
        public void Warning(string msg) => _source.LogWarning($"[AQWTranslation] {msg}");
        public void Error(string msg)   => _source.LogError($"[AQWTranslation] {msg}");

        public static void StaticError(string msg) =>
            _staticSource?.LogError($"[AQWTranslation] {msg}");

        public static void StaticWarning(string msg) =>
            _staticSource?.LogWarning($"[AQWTranslation] {msg}");

        public static void StaticDebug(string msg)
        {
            if (Core.ModConfig.VerboseLogging?.Value == true)
                _staticSource?.LogDebug($"[AQWTranslation] {msg}");
        }
    }
}
