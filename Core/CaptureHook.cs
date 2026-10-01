namespace AQWMod.Localization.Core
{
    public delegate void CaptureRecorder(
        string key, string rawOriginal, string category, string path,
        string[]? players, string[]? nums, string[]? maps,
        float now, string source);

    // Ponto de ligação entre os interceptores (que existem em toda edição do
    // mod) e a captura de textos observados (que só existe na edição Translator).
    // Sem ninguém ligado, Record não faz nada.
    public static class CaptureHook
    {
        public static CaptureRecorder? Recorder { get; set; }

        public static void Record(
            string key, string rawOriginal, string category, string path,
            string[]? players, string[]? nums, string[]? maps,
            float now, string source)
        {
            Recorder?.Invoke(key, rawOriginal, category, path, players, nums, maps, now, source);
        }
    }

    // Coletor usado quando a edição não captura strings sem tradução.
    public sealed class NullMissingTranslationCollector : IMissingTranslationCollector
    {
        public int PendingCount => 0;
        public void Capture(string text, TranslationContext? context) { }
        public void FlushToDisk() { }
    }
}
