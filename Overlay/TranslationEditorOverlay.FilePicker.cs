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
    // Seletor de arquivo de destino: painel único e reaproveitado, com a
    // mesma árvore de pastas/arquivos da janela principal (Tree.cs), mas em
    // modo "escolher" — cada arquivo vira um botão "Selecionar" que devolve o
    // caminho relativo via callback e fecha o painel, em vez de abrir uma
    // FileTableWindow. Pasta continua navegável e ganha o mesmo dropdown "+".
    // Dois usos: o botão "OK" de uma linha do Scan (força escolher um
    // arquivo real em vez de cair sempre em uncategorized.json) e o painel
    // "Mover" (MoveCopy.cs).
    public partial class TranslationEditorOverlay
    {
        private GameObject    _pickerPanel       = null!;
        private Text          _pickerTitle       = null!;
        private RectTransform _pickerListContent = null!;
        private InputField    _pickerFilterInput = null!;
        private readonly HashSet<string> _pickerExpandedFolders = new(StringComparer.Ordinal);
        // pra quem devolver o arquivo escolhido — null enquanto o painel
        // está fechado
        private Action<string>? _pickerOnFileSelected;
        // modo pasta (Mover arquivo/pasta da árvore): arquivos somem da
        // lista e cada pasta, mais a raiz, ganha o "Selecionar"
        private bool _pickerFolderMode;
        private GameObject _pickerRootSelectBtn = null!;
        private GameObject _pickerRootNewFileBtn = null!;

        private void BuildFilePicker(RectTransform canvasRoot)
        {
            _pickerPanel = Go("_AQWFilePicker", canvasRoot);
            var wrt = _pickerPanel.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(1150, -20);
            const float windowH = 560;
            wrt.sizeDelta        = new Vector2(420, windowH);
            Img(_pickerPanel, Bg);

            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _pickerPanel.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(6, 6, 6, 6);
            vl.spacing              = 2;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false; // mesmo tratamento de altura de BuildUI.cs (ver HRow())
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            const float hdrH = 26, filterRowH = 24, rootRowH = 26, footH = 26;

            var hdr = HRow(_pickerPanel, (int)hdrH, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            _pickerTitle = Lbl(hdr, "Escolher arquivo", 11, FontStyle.Bold, ColWhite, 0, true);
            Btn(hdr, "✕", 26, 22, BgClose, CloseFilePicker);

            var frow = HRow(_pickerPanel, (int)filterRowH, BgRow);
            Lbl(frow, "Buscar:", 10, FontStyle.Normal, ColGray, 48, false);
            _pickerFilterInput = Fld(frow, 0, 22, "Filtrar arquivos e pastas...", flex: true);
            _pickerFilterInput.onValueChanged.AddListener(_ => DoFilePickerTree());

            var rootRow = HRow(_pickerPanel, (int)rootRowH, Bg);
            Btn(rootRow, "+ Pasta", 62, 22, BgChip, () =>
                OpenCreateNodePanel("", isFolder: true, onCreated: OnPickerNodeCreated));
            _pickerRootNewFileBtn = Btn(rootRow, "+ Arquivo", 74, 22, BgChip, () =>
                OpenCreateNodePanel("", isFolder: false, onCreated: OnPickerNodeCreated)).gameObject;
            Lbl(rootRow, "", 9, FontStyle.Normal, ColGray, 0, true);
            _pickerRootSelectBtn = Btn(rootRow, "Selecionar raiz", 120, 22, BgBtn, () => PickerSelect("")).gameObject;

            const float vPad = 6 + 6, vSpacing = 2 * 4; // padding + 4 vãos (hdr/frow/rootRow/scroll/foot)
            float scrollH = windowH - vPad - vSpacing - hdrH - filterRowH - rootRowH - footH;
            _pickerListContent = BuildScrollView(_pickerPanel, scrollH);

            var foot = HRow(_pickerPanel, (int)footH, BgHdr);
            Btn(foot, "Cancelar", 90, 22, BgClose, CloseFilePicker);

            _pickerPanel.SetActive(false);
        }

        private void OpenFilePicker(string title, Action<string> onFileSelected) =>
            OpenPicker(title, onFileSelected, folderMode: false);

        // devolve o caminho relativo da pasta escolhida ("" = raiz)
        private void OpenFolderPicker(string title, Action<string> onFolderSelected) =>
            OpenPicker(title, onFolderSelected, folderMode: true);

        private void OpenPicker(string title, Action<string> onFileSelected, bool folderMode)
        {
            _pickerFolderMode        = folderMode;
            _pickerRootSelectBtn.SetActive(folderMode);
            _pickerRootNewFileBtn.SetActive(!folderMode);
            _pickerTitle.text        = title;
            _pickerOnFileSelected    = onFileSelected;
            _pickerFilterInput.text  = "";
            _pickerExpandedFolders.Clear();
            DoFilePickerTree();

            _pickerPanel.transform.SetAsLastSibling();
            _pickerPanel.SetActive(true);
        }

        private void PickerSelect(string relPath)
        {
            var cb = _pickerOnFileSelected;
            CloseFilePicker();
            cb?.Invoke(relPath);
        }

        private void CloseFilePicker()
        {
            _pickerPanel.SetActive(false);
            _pickerOnFileSelected = null;
        }

        // pasta nova só reabre a árvore já expandida nela; arquivo novo já
        // conta como a escolha feita
        private void OnPickerNodeCreated(string relPath, bool isFolder)
        {
            if (isFolder)
            {
                _pickerExpandedFolders.Add(relPath);
                DoFilePickerTree();
            }
            else if (!_pickerFolderMode)
            {
                PickerSelect(relPath);
            }
            else
            {
                DoFilePickerTree();
            }
        }

        private void DoFilePickerTree()
        {
            foreach (Transform t in _pickerListContent) Destroy(t.gameObject);

            var repo = TranslationManager.Instance.Repository;
            var root = FileTreeBuilder.BuildRoot(repo.Index.KeysByFile,
                FolderScanner.GetAllFolderPaths(TranslationManager.Instance.TranslationsRootPath));

            var query     = _pickerFilterInput.text;
            bool filtering = !string.IsNullOrWhiteSpace(query);
            if (filtering) root = FileTreeBuilder.Filter(root, query);
            // uncategorized.json não recebe mais tradução — nunca é destino
            root.Children.RemoveAll(c => c.IsFile && TreeNodeOps.IsReservedRootFile(c.RelativePath));

            if (root.Children.Count == 0)
            {
                Lbl(HRow(_pickerListContent.gameObject, 24, Bg),
                    filtering ? "Nenhum arquivo/pasta encontrado." : "Nenhum arquivo de tradução encontrado em translations/.",
                    10, FontStyle.Italic, ColGray, 0, true);
                return;
            }

            BuildFilePickerRows(root, depth: 0, forceExpand: filtering);
        }

        private void BuildFilePickerRows(FileTreeNode node, int depth, bool forceExpand)
        {
            foreach (var child in node.Children)
            {
                if (_pickerFolderMode && child.IsFile) continue;
                BuildFilePickerRow(child, depth, forceExpand);
                if (!child.IsFile && (forceExpand || _pickerExpandedFolders.Contains(child.RelativePath)))
                    BuildFilePickerRows(child, depth + 1, forceExpand);
            }
        }

        private void BuildFilePickerRow(FileTreeNode node, int depth, bool forceExpand)
        {
            var row = HRow(_pickerListContent.gameObject, 24, depth % 2 == 1 ? BgRowAlt : BgRow);

            if (depth > 0) Lbl(row, "", 9, FontStyle.Normal, ColGray, depth * 20, false);

            if (node.IsFile)
            {
                Lbl(row, node.Name, 10, FontStyle.Normal, ColWhite, 0, true);
                Lbl(row, $"{node.EntryCount}", 9, FontStyle.Italic, ColGray, 34, false);
                Btn(row, "Selecionar", 84, 20, BgBtn, () => PickerSelect(node.RelativePath));
            }
            else
            {
                bool expanded = forceExpand || _pickerExpandedFolders.Contains(node.RelativePath);
                Btn(row, expanded ? "▼" : "▶", 22, 20, BgChip, () =>
                {
                    if (!_pickerExpandedFolders.Remove(node.RelativePath))
                        _pickerExpandedFolders.Add(node.RelativePath);
                    DoFilePickerTree();
                });
                Lbl(row, node.Name + "/", 10, FontStyle.Bold, ColWhite, 0, true);
                Button plusBtn = null!;
                plusBtn = Btn(row, "+", 22, 20, BgChip, () =>
                    OpenCreateNodeDropdown(node.RelativePath, plusBtn.GetComponent<RectTransform>(), OnPickerNodeCreated));
                if (_pickerFolderMode)
                    Btn(row, "Selecionar", 84, 20, BgBtn, () => PickerSelect(node.RelativePath));
            }
        }
    }
}
