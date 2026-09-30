using System;
using System.Collections.Generic;
using System.Linq;

namespace AQWMod.Localization.Overlay
{
    // Monta a árvore de pastas/arquivos de translations/ a partir de
    // TranslationIndex.KeysByFile (já em memória, sem varredura de disco).
    // Puro dado, testável sem Unity — a construção de UI fica em Tree.cs.
    internal sealed class FileTreeNode
    {
        public string Name { get; }
        public string RelativePath { get; }   // sempre com '/', mesmo no Windows
        public bool IsFile { get; }
        public int EntryCount { get; }         // só relevante para arquivos
        public List<FileTreeNode> Children { get; } = new();

        public FileTreeNode(string name, string relativePath, bool isFile, int entryCount = 0)
        {
            Name = name;
            RelativePath = relativePath;
            IsFile = isFile;
            EntryCount = entryCount;
        }
    }

    internal static class FileTreeBuilder
    {
        /// <param name="extraFolders">
        /// Caminhos relativos de pasta sem nenhum arquivo dentro (ex.: pasta
        /// recém-criada pela GUI) — não aparecem em
        /// <paramref name="keysByFile"/>, então precisam ser injetadas à
        /// parte pra ficarem navegáveis até o usuário criar o primeiro
        /// arquivo dentro delas. Opcional.
        /// </param>
        public static FileTreeNode BuildRoot(
            IReadOnlyDictionary<string, IReadOnlyList<string>> keysByFile,
            IEnumerable<string>? extraFolders = null)
        {
            var root = new FileTreeNode("", "", isFile: false);

            foreach (var kv in keysByFile)
            {
                var segments = kv.Key.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0) continue;

                var parent   = EnsureFolderChain(root, segments, segments.Length - 1);
                var fileName = segments[segments.Length - 1];
                var relPath  = string.Join("/", segments);

                var existingFile = parent.Children.FirstOrDefault(
                    c => c.IsFile && string.Equals(c.Name, fileName, StringComparison.OrdinalIgnoreCase));
                if (existingFile == null)
                    parent.Children.Add(new FileTreeNode(fileName, relPath, isFile: true, kv.Value.Count));
            }

            if (extraFolders != null)
            {
                foreach (var folderPath in extraFolders)
                {
                    var segments = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0) continue;
                    EnsureFolderChain(root, segments, segments.Length);
                }
            }

            SortRecursively(root);
            return root;
        }

        /// <summary>
        /// Filtra a árvore por substring (case-insensitive) no nome de
        /// arquivo/pasta. Pasta cujo próprio nome bate mantém todos os
        /// filhos originais (buscar "guis" mostra a pasta guis/ inteira);
        /// senão, só os descendentes que batem (ou têm descendente que bate)
        /// sobrevivem, pra o caminho até um resultado profundo continuar
        /// visível. Query vazia/nula devolve a árvore original sem cópia.
        /// </summary>
        public static FileTreeNode Filter(FileTreeNode root, string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return root;
            return FilterRecursive(root, query.Trim()) ?? new FileTreeNode("", "", isFile: false);
        }

        private static FileTreeNode? FilterRecursive(FileTreeNode node, string query)
        {
            bool selfMatch = node.RelativePath.Length > 0 &&
                             node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            if (node.IsFile)
                return selfMatch ? node : null;

            if (selfMatch)
            {
                var wholeFolder = new FileTreeNode(node.Name, node.RelativePath, isFile: false);
                wholeFolder.Children.AddRange(node.Children);
                return wholeFolder;
            }

            var keptChildren = new List<FileTreeNode>();
            foreach (var child in node.Children)
            {
                var filteredChild = FilterRecursive(child, query);
                if (filteredChild != null) keptChildren.Add(filteredChild);
            }
            if (keptChildren.Count == 0) return null;

            var result = new FileTreeNode(node.Name, node.RelativePath, isFile: false);
            result.Children.AddRange(keptChildren);
            return result;
        }

        // Garante que existam nós de PASTA para os primeiros `folderDepth`
        // segmentos de `segments` (nunca cria nó de arquivo — quem chama
        // decide se/como o último segmento vira arquivo).
        private static FileTreeNode EnsureFolderChain(FileTreeNode root, string[] segments, int folderDepth)
        {
            var current = root;
            for (int i = 0; i < folderDepth; i++)
            {
                var relPath = string.Join("/", segments.Take(i + 1));
                var existing = current.Children.FirstOrDefault(
                    c => !c.IsFile && string.Equals(c.Name, segments[i], StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = new FileTreeNode(segments[i], relPath, isFile: false);
                    current.Children.Add(existing);
                }

                current = existing;
            }
            return current;
        }

        // Pastas primeiro (alfabético), depois arquivos (alfabético) — convenção
        // comum de explorador de arquivos, evita a lista parecer desordenada.
        private static void SortRecursively(FileTreeNode node)
        {
            node.Children.Sort((a, b) =>
            {
                if (a.IsFile != b.IsFile) return a.IsFile ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var child in node.Children) SortRecursively(child);
        }
    }
}
