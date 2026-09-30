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
    // Painel Mover entre arquivos: move as entradas marcadas (mesmo checkbox
    // "Sel." do Auto Traduzir) pra outro arquivo do corpus, preservando
    // context/ignore ao ler a entrada completa da origem antes de gravar no destino.
    public partial class TranslationEditorOverlay
    {
        private void BuildMovePanel(RectTransform canvasRoot)
        {
            _movePanel = Go("_AQWMovePanel", canvasRoot);
            Img(_movePanel, Bg);
            var wrt = _movePanel.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(710, -820);
            wrt.sizeDelta        = new Vector2(500, 150);

            // painel flutuante, sibling da janela principal — precisa entrar
            // na lista de bloqueio de mouse
            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _movePanel.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(8, 8, 6, 6);
            vl.spacing              = 4;
            vl.childControlWidth    = true;
            // este painel é desativado logo após construído — mesmo padrão
            // que causava linha gigante na janela principal/Abrir/Scan (ver HRow())
            vl.childControlHeight   = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            var hdr = HRow(_movePanel, 26, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            Lbl(hdr, "Mover marcadas", 11, FontStyle.Bold, ColWhite, 0, true);
            Btn(hdr, "✕", 26, 22, BgClose, () => _movePanel.SetActive(false));

            // destino escolhido via árvore (FilePicker.cs), mesmo seletor do
            // "OK" das linhas, aqui em modo "Mover"
            var destRow = HRow(_movePanel, 24, Bg);
            Lbl(destRow, "Destino:", 10, FontStyle.Normal, ColGray, 60, false);
            _moveDestLabel = Lbl(destRow, "(nenhum selecionado)", 10, FontStyle.Italic, ColGray, 0, true);
            Btn(destRow, "Escolher...", 90, 22, BgChip, () =>
                OpenFilePicker("Mover marcadas para...", relPath =>
                {
                    _moveDestRelPath      = relPath;
                    _moveDestLabel.text   = relPath;
                    _moveDestLabel.color  = ColWhite;
                    _moveDestLabel.fontStyle = FontStyle.Normal;
                }));

            var hintRow = HRow(_movePanel, 34, Bg);
            _moveHintText = Lbl(hintRow,
                "Move as linhas com checkbox \"Sel.\" marcado. Cria o arquivo se não existir.",
                9, FontStyle.Italic, ColGray, 0, true);

            var foot = HRow(_movePanel, 26, BgHdr);
            Btn(foot, "Mover",   80, 22, BgBtn,    MoveSelected);
            Btn(foot, "Cancelar", 90, 22, BgClose, () => _movePanel.SetActive(false));

            _movePanel.SetActive(false);
        }

        // chamado pelos botões "Mover" de cada janela (Scan/"Abrir" arquivo) —
        // lugar único pra resetar o destino escolhido a cada abertura, senão o
        // destino da vez anterior ficaria selecionado por engano
        private void OpenMovePanel(RowListPanel panel)
        {
            _moveSourcePanel         = panel;
            _moveDestRelPath         = null;
            _moveDestLabel.text      = "(nenhum selecionado)";
            _moveDestLabel.color     = ColGray;
            _moveDestLabel.fontStyle = FontStyle.Italic;
            _moveHintText.text       = "Move as linhas com checkbox \"Sel.\" marcado. Cria o arquivo se não existir.";

            // sobrepõe qualquer outra janela/painel já aberto — a ordem de
            // sibling num Canvas decide quem desenha por cima. O único que
            // fica acima dele é o seletor de arquivo (FilePicker.cs), que já
            // faz seu próprio SetAsLastSibling() quando aberto depois
            _movePanel.transform.SetAsLastSibling();
            _movePanel.SetActive(true);
        }

        private void MoveSelected()
        {
            var panel = _moveSourcePanel;
            if (panel == null) { _movePanel.SetActive(false); return; }

            if (string.IsNullOrEmpty(_moveDestRelPath))
            {
                _moveHintText.text = "Escolha um arquivo de destino primeiro (botão \"Escolher...\").";
                return;
            }
            var destRel = _moveDestRelPath;

            var selected = panel.Rows.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                _moveHintText.text = "Nenhuma linha marcada — marque o checkbox \"Sel.\" das linhas primeiro.";
                return;
            }

            var root     = TranslationManager.Instance.TranslationsRootPath;
            var destFull = Path.Combine(root, destRel.Replace('/', Path.DirectorySeparatorChar));

            int done = 0;
            var touchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { destFull };
            foreach (var row in selected)
            {
                var sourceFull = string.IsNullOrEmpty(row.SourceFile) ? OutputFile : row.SourceFile;
                if (string.Equals(Path.GetFullPath(sourceFull), Path.GetFullPath(destFull), StringComparison.OrdinalIgnoreCase))
                    continue; // já está no destino — nada a fazer

                // lê a entrada completa da origem pra preservar context/ignore,
                // em vez de confiar só no que a linha da GUI mostra
                var srcEntries = TranslationFileStore.Read(sourceFull);
                var srcEntry   = srcEntries.FirstOrDefault(e => e.Key == row.Original);
                var text       = srcEntry?.Text ?? row.TransFld.text;
                var context    = srcEntry?.Context;
                var ignore     = srcEntry?.IsIgnored ?? false;

                TranslationFileStore.Upsert(destFull, row.Original, text, context: context, ignore: ignore);
                TranslationFileStore.Remove(sourceFull, row.Original);

                touchedFiles.Add(sourceFull);
                SetRowSelected(panel, row, false);
                done++;
            }

            foreach (var f in touchedFiles)
                TranslationManager.Instance.ReloadFile(f);
            TMPTextPatch.InvalidateSafeTranslateCache();

            _movePanel.SetActive(false);
            panel.StatsLabel.text = $"Movidas {done} entrada(s) para '{destRel}'.";
            panel.RefreshList();
        }
    }
}
