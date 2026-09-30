using System.Reflection;
using BepInEx.Logging;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // Fora do jogo não existe BepInEx.Chainloader rodando, então Plugin.Log
    // (usado por TranslationLogger, exigido no construtor de
    // TranslationRepository) fica null. Em vez de mudar código de produção só
    // pra acomodar teste, cria um ManualLogSource solto (sem listener — os
    // métodos de log viram no-op, exatamente o que queremos) e injeta via
    // reflection no setter privado. [ModuleInitializer] faria isso automático
    // em .NET 5+, mas o projeto é net48 — daí o ICollectionFixture global,
    // aplicado a toda classe de teste que toca TranslationRepository/TMPTextPatch.
    public sealed class LogBootstrapFixture
    {
        public LogBootstrapFixture()
        {
            var prop = typeof(Plugin).GetProperty(
                "Log", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (prop?.GetValue(null) == null)
                prop?.SetValue(null, new ManualLogSource("AQWTranslation.Tests"));
        }
    }

    [CollectionDefinition("LogBootstrap")]
    public sealed class LogBootstrapCollection : ICollectionFixture<LogBootstrapFixture>
    {
    }
}
