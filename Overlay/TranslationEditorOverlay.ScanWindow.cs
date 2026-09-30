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
    // Janela standalone "Textos detectados na tela" — fica aberta ao mesmo
    // tempo que a árvore de pastas/arquivos, pra poder mover uma linha já
    // traduzida daqui pra um arquivo aberto. Reaproveita toda a lógica já
    // extraída e testada (EntryRowFilter, SelectionState, RowStatus,
    // EntryOrdering, PlaceholderHintFormatter) e os métodos generalizados via
    // RowListPanel — nenhuma duplicação de regra, só construção de UI e o
    // laço de varredura são próprios desta janela (CaptureRegistry continua
    // 100% compartilhado, é um registro único).
    public partial class TranslationEditorOverlay
    {
        private void BuildScanWindow(RectTransform canvasRoot)
        {
            _scanWindow = Go("ScanWindow", canvasRoot);
            var wrt = _scanWindow.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(720, -20);
            const float windowH = 630;
            wrt.sizeDelta        = new Vector2(680, windowH);
            Img(_scanWindow, Bg);

            // painel independente, precisa entrar na lista de bloqueio de
            // mouse, senão um clique aqui vazaria pro jogo por baixo
            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _scanWindow.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(6, 6, 6, 6);
            vl.spacing              = 2;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false; // cada linha já vem com altura fixa no RectTransform (ver HRow())
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            const float hdrH = 24, frowH = 26, colHdrH = 18, footH = 28;
            var hdr = HRow(_scanWindow, (int)hdrH, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            Lbl(hdr, "Textos detectados na tela", 11, FontStyle.Bold, ColWhite, 220, false);
            _scanStatsLabel = Lbl(hdr, "", 10, FontStyle.Normal, ColGray, 0, true);
            Btn(hdr, "↺", 30, 24, BgBtn, () =>
            {
                TMPTextPatch.InvalidateSafeTranslateCache();
                DoScanWindow();
            });
            Btn(hdr, "✕", 30, 24, BgClose, () => ToggleScanWindow(forceHide: true));

            // filtro "sem tradução" permanentemente ligado (Filter.UntranslatedOnly
            // no _scanPanel abaixo) — o objetivo desta janela é só mostrar o
            // que falta traduzir. Auto-refresh também sempre ligado, ver
            // QuickCheckAndRescanScanWindow().
            var frow = HRow(_scanWindow, (int)frowH, BgRow);
            Lbl(frow, "Buscar:", 10, FontStyle.Normal, ColGray, 48, false);
            _scanFilterInput = Fld(frow, 0, 22, "Filtrar textos...", flex: true);
            _scanFilterInput.onValueChanged.AddListener(_ => RefreshVisibility(_scanPanel));

            var accumBtn = Btn(frow, "⊕ Acum.", 64, 22, BgChip, () =>
            {
                _scanAccumulate = !_scanAccumulate;
                _scanImgAccum.color = _scanAccumulate ? BgChipOn : BgChip;
                DoScanWindow();
            });
            _scanImgAccum = accumBtn.GetComponentInChildren<Image>();

            _scanColumnHdr = HRow(_scanWindow, (int)colHdrH, BgHdr);
            Lbl(_scanColumnHdr, "Sel.", 9, FontStyle.Italic, ColGray, 26, false);
            Lbl(_scanColumnHdr, "Original (EN)",  9, FontStyle.Italic, ColGray, 184, false);
            Lbl(_scanColumnHdr, "Tradução PT-BR", 9, FontStyle.Italic, ColGray, 220, false);

            // altura = todo o espaço que sobra na janela
            const float vPad = 6 + 6, vSpacing = 2 * 4; // padding + 4 vãos (hdr/frow/colHdr/scroll/foot)
            float scrollH = windowH - vPad - vSpacing - hdrH - frowH - colHdrH - footH;
            _scanListContent = BuildScrollView(_scanWindow, scrollH);

            // checkbox "✓" de cada linha seleciona manualmente antes de Mover
            var foot = HRow(_scanWindow, (int)footH, BgHdr);
            var saveBtn = Btn(foot, "Salvar todas modificadas", 280, 24, BgBtn, () => SaveAll(_scanPanel));
            _scanSaveAllText = saveBtn.GetComponentInChildren<Text>();
            Btn(foot, "Mover", 70, 24, BgIgnBtn, () => OpenMovePanel(_scanPanel));

            _scanWindow.SetActive(false);

            _scanPanel = new RowListPanel
            {
                ListContent  = _scanListContent,
                FilterInput  = _scanFilterInput,
                StatsLabel   = _scanStatsLabel,
                SaveAllText  = _scanSaveAllText,
                ColumnHdr    = _scanColumnHdr,
                Filter       = new EntryRowFilter { UntranslatedOnly = true },
                Selection    = new SelectionState(),
                Rows         = new List<EntryRow>(),
                Buffers      = _buffers, // buffers de edição pendente são globais, mesma chave = mesmo texto pendente
                RefreshList  = DoScanWindow,
            };
        }

        private void ToggleScanWindow(bool? forceHide = null)
        {
            _scanVisible = forceHide == true ? false : !_scanVisible;
            _scanWindow.SetActive(_scanVisible);
            if (_scanVisible) DoScanWindow();
        }

        // mesma lógica de captura/exibição de DoScan() (Scan.cs), sem a
        // coordenação de ViewMode/Settings (não existem nesta janela).
        // ScanVisibleIntoRegistry/CaptureRegistry continuam 100%
        // compartilhados com a janela principal.
        private void DoScanWindow()
        {
            foreach (var row in _scanPanel.Rows)
                if (!string.IsNullOrWhiteSpace(row.TransFld.text))
                    _scanPanel.Buffers[row.Original] = row.TransFld.text;

            List<(string key, string rawOrig, string saved, string cat, string path, string sourceFile)> entries;
            Dictionary<string, string> localEdits;
            HashSet<string> visibleNow;
            float now = Time.realtimeSinceStartup;
            try
            {
                ScanVisibleIntoRegistry();

                localEdits = ReadFile();

                int cap = _scanAccumulate ? MaxHistory : MaxEntries;
                List<CaptureRegistry.Entry> regEntries = _scanAccumulate
                    ? CaptureRegistry.Snapshot()
                    : CaptureRegistry.RecentlySeen(now, RecentWindowSeconds);

                entries = regEntries
                    .OrderByDescending(e => e.LastSeen)
                    .Take(cap)
                    .Select(e =>
                    {
                        string saved = "";
                        try
                        {
                            if (!localEdits.TryGetValue(e.Key, out saved))
                                TranslationManager.Instance.Repository.TryGet(e.Key, out saved);
                        }
                        catch { saved = ""; }
                        var path = PlaceholderHintFormatter.Append(
                            e.Path, e.PlayerCaps, Array.Empty<string>(), e.NumCaps, e.MapCaps);
                        return (key: e.Key, rawOrig: e.RawOriginal, saved: saved ?? "", cat: e.Category, path: path, sourceFile: "");
                    })
                    .ToList();

                visibleNow = CollectVisibleTextSet();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[AQWTranslation] DoScanWindow abortado (lista preservada): {ex}");
                return;
            }

            foreach (Transform t in _scanPanel.ListContent) Destroy(t.gameObject);
            _scanPanel.Rows.Clear();
            PopulateRows(_scanPanel, _scanAccumulate, entries);

            _scanWindowLastSeenTexts.Clear();
            foreach (var k in visibleNow) _scanWindowLastSeenTexts.Add(k);
            _scanWindowLastRegistryCount = CaptureRegistry.Count;
        }

        // mesmos dois gatilhos de QuickCheckAndRescan() (registry cresceu /
        // tela visível mudou), rastreados de forma independente da janela principal
        private void QuickCheckAndRescanScanWindow()
        {
            // pausa enquanto o seletor de arquivo está aberto — DoScanWindow()
            // destrói e recria todo EntryRow a cada rescan; se rodasse
            // enquanto o usuário escolhe onde salvar (botão "OK" -> seletor),
            // o EntryRow que o seletor ainda vai usar no callback ficaria
            // órfão. SaveEntryWithText já captura o texto antes de abrir o
            // seletor, então a gravação em si é imune, mas evitar o rescan
            // aqui evita a causa, não só o sintoma.
            if (_pickerPanel != null && _pickerPanel.activeSelf) return;

            var sel = EventSystem.current?.currentSelectedGameObject;
            if (sel != null && sel.GetComponent<InputField>() is InputField fld && fld.isFocused
                && sel.transform.IsChildOf(transform))
                return;

            if (CaptureRegistry.Count != _scanWindowLastRegistryCount)
            {
                DoScanWindow();
                return;
            }

            var current = CollectVisibleTextSet();
            if (!current.SetEquals(_scanWindowLastSeenTexts))
                DoScanWindow();
        }
    }
}
