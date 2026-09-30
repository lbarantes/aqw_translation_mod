using System;
using System.Collections.Generic;

namespace AQWMod.Localization.Overlay
{
    // Estado e decisão de "esta linha passa no filtro atual?" da lista
    // principal (busca + categoria + "sem tradução").
    internal sealed class EntryRowFilter
    {
        public bool UntranslatedOnly { get; set; }

        public readonly HashSet<string> SelectedCategories = new HashSet<string>(StringComparer.Ordinal);

        public bool Matches(
            string original, string translationBuffer, string rawOriginal,
            bool isTranslated, bool isIgnored, string category, string searchText)
        {
            if (UntranslatedOnly && (isTranslated || isIgnored))
                return false;

            if (searchText.Length > 0)
            {
                bool matchesSearch =
                    original.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    translationBuffer.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (rawOriginal.Length > 0 &&
                     rawOriginal.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!matchesSearch) return false;
            }

            if (SelectedCategories.Count > 0 && !SelectedCategories.Contains(category))
                return false;

            return true;
        }
    }
}
