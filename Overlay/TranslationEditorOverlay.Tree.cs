using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AQWMod.Localization.Core;
using AQWMod.Localization.Interceptors;
using AQWMod.Localization.Capture;
using AQWMod.Localization.Repository;
using AQWMod.Localization.Translation;

namespace AQWMod.Localization.Overlay
{
    // Árvore de pastas/arquivos: navegação (expandir/colapsar), abrir arquivo
    // numa tabela ("Abrir") e criar pasta/arquivo (dropdown "+", ver
    // CreateNode.cs). Dado vem de TranslationIndex.KeysByFile (já em memória)
    // via FileTreeBuilder.cs; pasta genuinamente vazia (sem arquivo
    // carregado) é injetada à parte via FolderScanner.cs, uma varredura leve
    // de diretórios em disco.
    public partial class TranslationEditorOverlay
    {
        // pastas expandidas no momento, por caminho relativo — sobrevive a
        // reconstruções da árvore (refeita do zero a cada expandir/colapsar)
        private readonly HashSet<string> _treeExpandedFolders = new(StringComparer.Ordinal);

        private void DoTree()
        {
            _viewMode     = ViewMode.Tree;
            if (_imgSettings != null) _imgSettings.color = BgChip;

            foreach (Transform t in _listContent) Destroy(t.gameObject);

            var repo = TranslationManager.Instance.Repository;
            var root = FileTreeBuilder.BuildRoot(repo.Index.KeysByFile,
                FolderScanner.GetAllFolderPaths(TranslationManager.Instance.TranslationsRootPath));

            // busca filtra arquivo/pasta por nome. Enquanto filtrando, mostra
            // tudo expandido, ignorando o estado de expandir/colapsar do
            // usuário — senão um resultado dentro de pasta colapsada ficaria invisível
            var query     = _filterInput != null ? _filterInput.text : "";
            bool filtering = !string.IsNullOrWhiteSpace(query);
            if (filtering) root = FileTreeBuilder.Filter(root, query);

            int fileCount   = CountNodes(root, wantFiles: true);
            int folderCount = CountNodes(root, wantFiles: false);
            _statsLabel.text = filtering
                ? $"{fileCount} resultado(s) para \"{query}\""
                : $"{fileCount} arquivo(s) em {folderCount} pasta(s)";

            if (root.Children.Count == 0)
            {
                Lbl(HRow(_listContent.gameObject, 24, Bg),
                    filtering ? "Nenhum arquivo/pasta encontrado." : "Nenhum arquivo de tradução encontrado em translations/.",
                    10, FontStyle.Italic, ColGray, 0, true);
                return;
            }

            BuildTreeRows(root, depth: 0, forceExpand: filtering);
        }

        private static int CountNodes(FileTreeNode node, bool wantFiles)
        {
            int count = node.IsFile == wantFiles ? 1 : 0;
            if (node.RelativePath.Length == 0) count = 0; // raiz não conta como pasta
            foreach (var child in node.Children) count += CountNodes(child, wantFiles);
            return count;
        }

        private void BuildTreeRows(FileTreeNode node, int depth, bool forceExpand)
        {
            foreach (var child in node.Children)
            {
                BuildTreeRow(child, depth, forceExpand);
                if (!child.IsFile && (forceExpand || _treeExpandedFolders.Contains(child.RelativePath)))
                    BuildTreeRows(child, depth + 1, forceExpand);
            }
        }

        private void BuildTreeRow(FileTreeNode node, int depth, bool forceExpand)
        {
            var row = HRow(_listContent.gameObject, 24, depth % 2 == 1 ? BgRowAlt : BgRow);

            // indentação: espaçador sem texto, largura proporcional à profundidade
            if (depth > 0) Lbl(row, "", 9, FontStyle.Normal, ColGray, depth * 20, false);

            if (node.IsFile)
            {
                Lbl(row, node.Name, 10, FontStyle.Normal, ColWhite, 0, true);
                Lbl(row, $"{node.EntryCount} entrada(s)", 9, FontStyle.Italic, ColGray, 90, false);
                Btn(row, "Abrir", 56, 20, BgBtn, () => OpenFileTable(node.RelativePath));
            }
            else
            {
                bool expanded = forceExpand || _treeExpandedFolders.Contains(node.RelativePath);
                Btn(row, expanded ? "▼" : "▶", 22, 20, BgChip, () =>
                {
                    if (!_treeExpandedFolders.Remove(node.RelativePath))
                        _treeExpandedFolders.Add(node.RelativePath);
                    DoTree();
                });
                Lbl(row, node.Name + "/", 10, FontStyle.Bold, ColWhite, 0, true);
                // captura plusBtn no próprio closure — válido, só é lido
                // depois que Btn() retorna e a variável já foi atribuída
                Button plusBtn = null!;
                plusBtn = Btn(row, "+", 22, 20, BgChip,
                    () => OpenCreateNodeDropdown(node.RelativePath, plusBtn.GetComponent<RectTransform>()));
            }
        }
    }
}
