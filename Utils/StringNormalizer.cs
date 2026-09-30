using System.Text.RegularExpressions;

namespace AQWMod.Localization.Utils
{
    // Normaliza strings antes do lookup, pra variação de whitespace/quebra de
    // linha não impedir o match no repositório.
    public static class StringNormalizer
    {
        private static readonly Regex CrLfRegex =
            new Regex(@"\r\n|\r", RegexOptions.Compiled);

        private static readonly Regex MultiSpaceRegex =
            new Regex(@"[^\S\n]{2,}", RegexOptions.Compiled);

        private static readonly Regex AllWhitespaceRegex =
            new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>Unifica quebras de linha e colapsa espaços múltiplos, preservando quebras simples.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = CrLfRegex.Replace(text, "\n");
            text = MultiSpaceRegex.Replace(text, " ");
            return text.Trim();
        }

        /// <summary>Colapsa tudo, inclusive quebras de linha. Usada só em comparações de fallback.</summary>
        public static string NormalizeAggressive(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return AllWhitespaceRegex.Replace(text.Trim(), " ");
        }
    }
}
