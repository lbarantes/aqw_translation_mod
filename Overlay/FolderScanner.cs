using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AQWMod.Localization.Overlay
{
    // Enumera pastas reais em disco sob translations/. Existe pra cobrir um
    // caso que TranslationIndex.KeysByFile nunca alcança: uma pasta sem
    // nenhum arquivo de tradução carregado (vazia, ou só com subpastas
    // igualmente vazias) — sem isso, a árvore da GUI só mostrava essa pasta
    // na mesma sessão em que foi criada e ela sumia depois de reiniciar o
    // jogo. FileTreeBuilder.BuildRoot já sabe reusar o nó se a pasta também
    // aparece via keysByFile, então é seguro devolver todas as pastas do
    // disco aqui, não só as vazias.
    internal static class FolderScanner
    {
        /// <summary>
        /// Caminhos relativos (sempre com '/') de toda pasta sob
        /// <paramref name="translationsRoot"/>, exceto as internas do mod
        /// (qualquer segmento começando com "_" — mesma convenção de
        /// TranslationRepository.LoadAll/IngameEditsMigrator). Devolve
        /// vazio se a raiz não existir. Ordem não é garantida.
        /// </summary>
        public static IEnumerable<string> GetAllFolderPaths(string translationsRoot)
        {
            if (string.IsNullOrEmpty(translationsRoot) || !Directory.Exists(translationsRoot))
                yield break;

            foreach (var dir in Directory.GetDirectories(translationsRoot, "*", SearchOption.AllDirectories))
            {
                var rel = GetRelativeFolderPath(dir, translationsRoot);
                if (rel == null) continue;
                if (rel.Split('/').Any(seg => seg.StartsWith("_", StringComparison.Ordinal))) continue;
                yield return rel;
            }
        }

        internal static string? GetRelativeFolderPath(string fullPath, string rootPath)
        {
            try
            {
                var rootUri = new Uri(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
                var dirUri  = new Uri(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(dirUri).ToString()).TrimEnd('/');
            }
            catch { return null; }
        }
    }
}
