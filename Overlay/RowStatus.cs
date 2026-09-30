namespace AQWMod.Localization.Overlay
{
    internal enum RowStatusKind { Translated, Ignored, Missing }

    // Decide se uma entrada está traduzida/ignorada/faltando a partir do
    // texto original e do valor salvo: salvo não-vazio e diferente do
    // original = traduzido; salvo não-vazio e igual = ignorado; senão, falta.
    internal readonly struct RowStatus
    {
        public RowStatusKind Kind { get; }
        public string Label { get; }

        private RowStatus(RowStatusKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public static RowStatus Compute(string original, string saved)
        {
            bool hasSaved = !string.IsNullOrEmpty(saved);

            if (hasSaved && saved != original)
                return new RowStatus(RowStatusKind.Translated, "[T]");

            if (hasSaved && saved == original)
                return new RowStatus(RowStatusKind.Ignored, "[~]");

            return new RowStatus(RowStatusKind.Missing, "[ ]");
        }
    }
}
