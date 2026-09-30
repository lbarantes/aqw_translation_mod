using System;

namespace AQWMod.Localization.Core
{
    // Identidade de uma tradução: texto normalizado + um contexto OPCIONAL de
    // desambiguação (ex.: "skill", "ui.button"), pro caso raro em que o mesmo
    // texto-fonte precisa de traduções diferentes em lugares diferentes.
    //
    // ToString() produz "texto@@contexto" só pra exibição/log — a igualdade
    // real compara os dois campos separados (Ordinal), nunca a string
    // concatenada, pra não colidir se um texto real tiver "@@" dentro.
    public readonly struct TranslationKey : IEquatable<TranslationKey>
    {
        public string Text { get; }
        public string? Context { get; }

        public TranslationKey(string text, string? context = null)
        {
            Text = text;
            Context = string.IsNullOrEmpty(context) ? null : context;
        }

        public bool Equals(TranslationKey other) =>
            string.Equals(Text, other.Text, StringComparison.Ordinal) &&
            string.Equals(Context, other.Context, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is TranslationKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Text?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ (Context?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString() => Context == null ? Text : $"{Text}@@{Context}";

        /// <summary>Reconstrói a partir da forma "texto" ou "texto@@contexto" (inverso de ToString()).</summary>
        public static TranslationKey Parse(string composite)
        {
            var sep = composite.IndexOf("@@", StringComparison.Ordinal);
            return sep >= 0
                ? new TranslationKey(composite.Substring(0, sep), composite.Substring(sep + 2))
                : new TranslationKey(composite);
        }

        public static bool operator ==(TranslationKey left, TranslationKey right) => left.Equals(right);
        public static bool operator !=(TranslationKey left, TranslationKey right) => !left.Equals(right);
    }
}
