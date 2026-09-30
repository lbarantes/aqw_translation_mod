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
    // Construção da janela principal — cria toda a hierarquia de GameObjects do overlay.
    public partial class TranslationEditorOverlay
    {
        private void BuildUI()
        {
            var cgo = new GameObject("_AQWOverlayCanvas");
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
            var canvasRoot = canvas.GetComponent<RectTransform>();

            _window = Go("Window", canvasRoot);
            var wrt = _window.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(20, -20);
            const float windowH = 630;
            wrt.sizeDelta        = new Vector2(680, windowH);
            InputBlockerPatch.OverlayWindow = wrt;
            InputBlockerPatch.OverlayRoot   = cgo.transform;
            Img(_window, Bg);

            var vl = _window.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(6, 6, 6, 6);
            vl.spacing              = 2;
            vl.childControlWidth    = true;
            // altura de cada filho direto da janela vem fixada no próprio
            // RectTransform (sizeDelta em HRow()/BuildScrollView()) — deixar
            // este grupo controlar a altura reabre a corrida de rebuild em
            // objeto inativo que deixava o cabeçalho gigante (ver HRow())
            vl.childControlHeight   = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            const float hdrH = 24, frowH = 26;
            var hdr = HRow(_window, (int)hdrH, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            Lbl(hdr, "AQW Translation Editor [F10]", 11, FontStyle.Bold, ColWhite, 220, false);
            _statsLabel = Lbl(hdr, "", 10, FontStyle.Normal, ColGray, 0, true);
            Btn(hdr, "↺", 30, 24, BgBtn, () =>
            {
                // descarta o cache por componente, textos já na tela que
                // ganharam tradução nova são re-traduzidos no próximo setter
                TMPTextPatch.InvalidateSafeTranslateCache();
                if (_viewMode != ViewMode.Settings) DoTree();
            });
            var cfgBtn = Btn(hdr, "⚙", 30, 24, BgChip, ToggleSettings);
            _imgSettings = cfgBtn.GetComponent<Image>();
            Btn(hdr, "✕", 30, 24, BgClose, () => SetVisible(false));

            // "Buscar" filtra arquivo/pasta da árvore por nome — a lógica de
            // filtro real vive em DoTree()
            var frow = HRow(_window, (int)frowH, BgRow);
            Lbl(frow, "Buscar:", 10, FontStyle.Normal, ColGray, 48, false);
            _filterInput = Fld(frow, 0, 22, "Filtrar arquivos e pastas...", flex: true);
            _filterInput.onValueChanged.AddListener(_ => { if (_viewMode == ViewMode.Tree) DoTree(); });

            // abre a janela standalone de Scan (RowListPanel próprio),
            // independente desta janela, pra poder ficar aberta junto com a árvore
            Btn(frow, "Textos detectados", 130, 22, BgIgnBtn, () => ToggleScanWindow());

            // criar pasta/arquivo na raiz — botão equivalente por pasta fica
            // na própria linha da árvore (Tree.cs)
            Btn(frow, "+ Pasta", 62, 22, BgChip, () => { DoTree(); OpenCreateNodePanel("", isFolder: true); });
            Btn(frow, "+ Arquivo", 74, 22, BgChip, () => { DoTree(); OpenCreateNodePanel("", isFolder: false); });

            // altura = todo o espaço que sobra na janela, senão sobra uma
            // margem vazia grande no fim da caixa
            const float vPad = 6 + 6, vSpacing = 2 * 2; // padding + 2 vãos (hdr/frow/scroll)
            float scrollH = windowH - vPad - vSpacing - hdrH - frowH;
            _listContent = BuildScrollView(_window, scrollH);

            _window.SetActive(false);

            BuildDetailPanel(canvasRoot);
            BuildAutoPreviewPanel(canvasRoot);
            BuildMovePanel(canvasRoot);
            BuildCreateNodePanel(canvasRoot);
            BuildCreateNodeDropdown(canvasRoot);
            BuildFilePicker(canvasRoot);
            BuildScanWindow(canvasRoot);

            LogSpriteResolutionOnce();
        }
    }
}
