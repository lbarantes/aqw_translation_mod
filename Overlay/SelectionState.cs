using System;
using System.Collections.Generic;

namespace AQWMod.Localization.Overlay
{
    // Quais linhas estão marcadas pro Auto Traduzir / Mover-Copiar em lote,
    // sobrevivendo a reconstruções da lista (Refresh/DoScan/DoLibrary
    // destroem e recriam EntryRow, mas a seleção não pode se perder nesse
    // meio-tempo).
    internal sealed class SelectionState
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        // sem separador entre sourceFile e original — um path de arquivo
        // nunca colide com o texto original de uma tradução
        private static string Key(string sourceFile, string original) => sourceFile + original;

        public bool Contains(string sourceFile, string original) => _keys.Contains(Key(sourceFile, original));

        public void Add(string sourceFile, string original) => _keys.Add(Key(sourceFile, original));

        public void Remove(string sourceFile, string original) => _keys.Remove(Key(sourceFile, original));

        public void Clear() => _keys.Clear();
    }
}
