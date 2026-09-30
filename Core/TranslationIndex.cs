using System.Collections.Generic;

namespace AQWMod.Localization.Core
{
    // Proveniência arquivo <-> chave, montada no mesmo passe de carregamento
    // do TranslationRepository (não é uma segunda leitura de disco). A GUI usa
    // isso pra navegar por pasta/arquivo sem reimplementar a leitura.
    //
    // SourceFile: chave -> arquivo que venceu a resolução (prioridade por camada).
    // KeysByFile: arquivo -> todas as chaves declaradas nele, mesmo as que
    //   perderam um conflito — útil pra GUI mostrar o arquivo como ele
    //   realmente está em disco, não só o que "venceu" em runtime.
    public sealed class TranslationIndex
    {
        public IReadOnlyDictionary<string, string> SourceFile { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> KeysByFile { get; }

        public TranslationIndex(
            IReadOnlyDictionary<string, string> sourceFile,
            IReadOnlyDictionary<string, IReadOnlyList<string>> keysByFile)
        {
            SourceFile = sourceFile;
            KeysByFile = keysByFile;
        }

        public static readonly TranslationIndex Empty = new TranslationIndex(
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>());
    }
}
