using System;

namespace AQWMod.Localization.Overlay
{
    // Ordem de exibição das linhas: sem tradução primeiro, depois ignorado,
    // depois traduzido — alfabético dentro de cada grupo.
    internal static class EntryOrdering
    {
        public static int Priority(string saved, string original) =>
            string.IsNullOrEmpty(saved) ? 0 : saved == original ? 1 : 2;

        public static int Compare(string keyA, string savedA, string keyB, string savedB)
        {
            int pa = Priority(savedA, keyA);
            int pb = Priority(savedB, keyB);
            return pa != pb ? pa - pb : StringComparer.OrdinalIgnoreCase.Compare(keyA, keyB);
        }
    }
}
