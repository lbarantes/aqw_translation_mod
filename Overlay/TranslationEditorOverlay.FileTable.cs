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
    // Clicar "Abrir" numa linha de arquivo da árvore abre uma janela própria
    // e independente mostrando as traduções daquele arquivo, reaproveitando
    // toda a máquina de linhas já usada em Scan/lista principal (BuildRow/
    // SaveEntry/IgnoreEntry/Del/SaveAll/... — nenhuma lógica de negócio nova,
    // só mais uma instância de RowListPanel). Múltiplos arquivos podem ficar
    // abertos ao mesmo tempo, pra poder escolher entre vários ao mover uma
    // linha do Scan.
    public partial class TranslationEditorOverlay
    {
        private void OpenFileTable(string relativePath)
        {
            if (_openFileTables.TryGetValue(relativePath, out var existing))
            {
                // já aberto, só traz pra frente (sibling por último = por cima)
                existing.Root.transform.SetAsLastSibling();
                if (_detailPanel != null && _detailPanel.activeSelf) _detailPanel.transform.SetAsLastSibling();
                if (_movePanel != null && _movePanel.activeSelf) _movePanel.transform.SetAsLastSibling();
                return;
            }

            var fullPath = Path.Combine(
                TranslationManager.Instance.TranslationsRootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            var win = BuildFileTableWindow(relativePath, fullPath);
            _openFileTables[relativePath] = win;
            PopulateFileTable(win);

            // janela nova entra como último sibling (por cima de tudo) —
            // devolve Detail/Mover à frente se estavam abertos
            if (_detailPanel != null && _detailPanel.activeSelf) _detailPanel.transform.SetAsLastSibling();
            if (_movePanel != null && _movePanel.activeSelf) _movePanel.transform.SetAsLastSibling();
        }

        private void CloseFileTable(string relativePath)
        {
            if (!_openFileTables.TryGetValue(relativePath, out var win)) return;

            if (win.PopulateCoroutine != null) StopCoroutine(win.PopulateCoroutine);

            var rt = win.Root.GetComponent<RectTransform>();
            InputBlockerPatch.ExtraBlockedPanels.Remove(rt);
            if (_moveSourcePanel == win.Panel) _moveSourcePanel = null;
            if (_autoPreviewSourcePanel == win.Panel) _autoPreviewSourcePanel = null;
            if (_detailSourcePanel == win.Panel) _detailSourcePanel = null;

            Destroy(win.Root);
            _openFileTables.Remove(relativePath);
        }

        private OpenFileTableWindow BuildFileTableWindow(string relativePath, string fullPath)
        {
            var canvasRoot = _scanWindow.transform.parent as RectTransform;

            var root = Go("FileTableWindow", canvasRoot!);
            var wrt = root.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            // cascata simples, pra não empilhar todas as janelas no mesmo lugar
            int cascade = _openFileTables.Count % 6;
            wrt.anchoredPosition = new Vector2(360 + cascade * 28, -140 - cascade * 28);
            const float windowH = 560;
            wrt.sizeDelta        = new Vector2(600, windowH);
            Img(root, Bg);

            // painel flutuante independente, precisa entrar na lista de
            // bloqueio de mouse, senão um clique aqui vazaria pro jogo por baixo
            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = root.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(6, 6, 6, 6);
            vl.spacing              = 2;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false; // altura de cada linha já vem fixa no RectTransform (ver HRow())
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            const float hdrH = 28, frowH = 26, colHdrH = 18, footH = 28;
            var hdr = HRow(root, (int)hdrH, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            Lbl(hdr, Shorten(relativePath, 40), 10, FontStyle.Bold, ColWhite, 0, true);
            var statsLabel = Lbl(hdr, "", 9, FontStyle.Normal, ColGray, 0, false);
            Btn(hdr, "✕", 26, 22, BgClose, () => CloseFileTable(relativePath));

            var frow = HRow(root, (int)frowH, BgRow);
            Lbl(frow, "Buscar:", 10, FontStyle.Normal, ColGray, 48, false);
            var filterInput = Fld(frow, 0, 22, "Filtrar textos...", flex: true);

            var columnHdr = HRow(root, (int)colHdrH, BgHdr);
            Lbl(columnHdr, "Sel.", 9, FontStyle.Italic, ColGray, 26, false);
            Lbl(columnHdr, "Original (EN)",  9, FontStyle.Italic, ColGray, 184, false);
            Lbl(columnHdr, "Tradução PT-BR", 9, FontStyle.Italic, ColGray, 220, false);

            // altura = todo o espaço que sobra na janela, é a única linha
            // que se beneficia de espaço extra (mais linhas visíveis sem rolar)
            const float vPad = 6 + 6, vSpacing = 2 * 4;
            float scrollH = windowH - vPad - vSpacing - hdrH - frowH - colHdrH - footH;
            var listContent = BuildScrollView(root, scrollH);

            // checkbox "✓" de cada linha seleciona manualmente antes de Mover
            var foot = HRow(root, (int)footH, BgHdr);
            var saveBtn = Btn(foot, "Salvar todas modificadas", 280, 24, BgBtn, () => SaveAll(_openFileTables[relativePath].Panel));
            var saveAllText = saveBtn.GetComponentInChildren<Text>();
            Btn(foot, "Mover", 70, 24, BgIgnBtn, () => OpenMovePanel(_openFileTables[relativePath].Panel));

            var win = new OpenFileTableWindow
            {
                RelativePath = relativePath,
                FullPath     = fullPath,
                Root         = root,
            };

            var panel = new RowListPanel
            {
                ListContent  = listContent,
                FilterInput  = filterInput,
                StatsLabel   = statsLabel,
                SaveAllText  = saveAllText,
                ColumnHdr    = columnHdr,
                Filter       = new EntryRowFilter(),
                Selection    = new SelectionState(),
                Rows         = new List<EntryRow>(),
                Buffers      = _buffers, // mesmo buffer global de edição pendente
                RefreshList  = () => PopulateFileTable(win),
            };
            win.Panel = panel;

            filterInput.onValueChanged.AddListener(_ => RefreshVisibility(panel));

            return win;
        }

        // constrói em lote por frame via coroutine (não scroll virtualizado
        // — a lista inteira é montada, só espalhada ao longo de alguns
        // frames), pra não travar num arquivo grande: cada linha monta ~19
        // GameObjects, então construir tudo síncrono derruba o FPS.
        private const int FileTableRowsPerFrame = 40;

        private void PopulateFileTable(OpenFileTableWindow win)
        {
            if (win.PopulateCoroutine != null) StopCoroutine(win.PopulateCoroutine);

            foreach (Transform t in win.Panel.ListContent) Destroy(t.gameObject);
            win.Panel.Rows.Clear();

            var entries = FileTableEntryListBuilder.Build(win.RelativePath, win.FullPath);
            win.Panel.StatsLabel.text = $"Carregando {entries.Count} entrada(s)...";

            win.PopulateCoroutine = StartCoroutine(PopulateFileTableIncremental(win, entries));
        }

        private IEnumerator PopulateFileTableIncremental(OpenFileTableWindow win,
            List<(string key, string rawOrig, string saved, string cat, string path, string sourceFile)> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                // a janela pode ter sido fechada (Destroy) no meio da construção
                if (!_openFileTables.ContainsKey(win.RelativePath) || win.Root == null) yield break;

                var (key, rawOrig, saved, cat, path, sourceFile) = entries[i];
                var buf = win.Panel.Buffers.TryGetValue(key, out var b) ? b : saved;
                win.Panel.Rows.Add(BuildRow(win.Panel, key, rawOrig, saved, cat, path, buf, i % 2 == 1, sourceFile));

                if (i % FileTableRowsPerFrame == FileTableRowsPerFrame - 1)
                    yield return null;
            }

            win.Panel.StatsLabel.text = $"{entries.Count} entrada(s)";
            RefreshVisibility(win.Panel);
            UpdateSaveAllBtn(win.Panel);
            win.PopulateCoroutine = null;
        }
    }
}
