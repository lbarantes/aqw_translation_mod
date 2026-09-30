using System.Collections.Generic;
using System.Linq;
using AQWMod.Localization.Repository;

namespace AQWMod.Localization.Overlay
{
    // Monta a lista ordenada de entradas de UM arquivo específico (a tabela
    // que abre ao clicar "Abrir" na árvore). Usa TranslationFileStore.Read
    // direto — lê o arquivo puro, sem passar pelas camadas de prioridade do
    // repositório, porque aqui o usuário edita este arquivo, não resolve
    // qual tradução vence em runtime.
    internal static class FileTableEntryListBuilder
    {
        public static List<(string key, string rawOrig, string saved, string cat, string path, string sourceFile)> Build(
            string relativePath, string fullPath)
        {
            var list = TranslationFileStore.Read(fullPath)
                .Select(e => (
                    key: e.Key,
                    rawOrig: e.Key,
                    saved: e.IsIgnored ? e.Key : e.Text,
                    cat: "",
                    path: relativePath,
                    sourceFile: fullPath))
                .ToList();

            list.Sort((a, b) => EntryOrdering.Compare(a.key, a.saved, b.key, b.saved));
            return list;
        }
    }
}
