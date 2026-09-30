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
    // Auto Tradução em lote — orquestração, preview e aplicação (Fase 5).
    public partial class TranslationEditorOverlay
    {
        // Fluxo: "Auto Traduzir" traduz as linhas marcadas pelo checkbox
        // "Sel." -> preview com uma linha por entrada, cada uma com checkbox
        // próprio -> "Aplicar marcadas" grava só o que estiver marcado. Nada
        // é escrito em disco antes disso.
        //
        // Segurança: entrada que já tinha tradução manual vem desmarcada por
        // padrão (sobrescrever exige marcar explicitamente); entrada cuja
        // contagem de "{placeholder}" mudou depois da tradução vem com
        // checkbox desabilitado, nunca aplicável direto — evita corromper um
        // {player}/{n1}/{map}/{npc}.

        // marca/desmarca em lote — sem isto, selecionar muitas linhas
        // exigiria clicar checkbox por checkbox
        private void SelectAllVisible(RowListPanel panel)
        {
            foreach (var row in panel.Rows)
            {
                if (row.Root == null || !row.Root.activeSelf || row.Selected) continue;
                SetRowSelected(panel, row, true);
            }
        }

        private void ClearSelection(RowListPanel panel)
        {
            foreach (var row in panel.Rows)
                if (row.Selected) SetRowSelected(panel, row, false);
            panel.Selection.Clear();
        }

        private void SetRowSelected(RowListPanel panel, EntryRow row, bool selected)
        {
            row.Selected = selected;
            if (selected) panel.Selection.Add(row.SourceFile, row.Original);
            else panel.Selection.Remove(row.SourceFile, row.Original);
            // SelImg é o "Fill" do botão; o rótulo de texto é irmão dele, não
            // filho (ver Btn() em UIHelpers.cs)
            var t = row.SelImg?.transform.parent.GetComponentInChildren<Text>();
            if (t != null) t.text = selected ? "✓" : "";
            if (row.SelImg != null) row.SelImg.color = selected ? BgChipOn : BgChip;
        }

        private AutoTranslationOrchestrator GetOrCreateOrchestrator()
        {
            if (_autoOrchestrator != null) return _autoOrchestrator;
            ITranslationProvider provider = ModConfig.AutoTranslateEnabled.Value
                ? new MyMemoryTranslationProvider()
                : new ManualTranslationProvider();
            _autoOrchestrator = new AutoTranslationOrchestrator(provider);
            return _autoOrchestrator;
        }

        private void OnAutoTranslateClicked(RowListPanel panel)
        {
            if (_autoBusy)
            {
                // segundo clique enquanto ocupado = cancelar a busca em andamento
                _autoCts?.Cancel();
                return;
            }

            if (!ModConfig.AutoTranslateEnabled.Value)
            {
                panel.StatsLabel.text = "Auto Tradução desativada em Configurações.";
                return;
            }

            // só as linhas explicitamente marcadas — nunca escolhe sozinho o que traduzir
            var allSelected = panel.Rows.Where(r => r.Selected).ToList();

            if (allSelected.Count == 0)
            {
                panel.StatsLabel.text = "Nenhuma linha marcada. Marque o checkbox das linhas que quer auto-traduzir primeiro.";
                return;
            }

            var candidates = allSelected.Take(MaxAutoTranslateBatch).ToList();
            if (allSelected.Count > MaxAutoTranslateBatch)
                panel.StatsLabel.text = $"{allSelected.Count} marcadas — processando as primeiras {MaxAutoTranslateBatch} agora.";

            _ = RunAutoTranslateAsync(panel, candidates);
        }

        private async Task RunAutoTranslateAsync(RowListPanel panel, List<EntryRow> selectedRows)
        {
            _autoBusy = true;
            _autoCts  = new CancellationTokenSource();

            try
            {
                var orchestrator = GetOrCreateOrchestrator();
                // usa r.Original (chave já normalizada, com {player}/{n1}/
                // {map}/{npc} como tokens), nunca r.RawOriginal — no Scan,
                // RawOriginal tem o valor real capturado (username de
                // verdade), não o token. Mandar isso pra API grudaria um
                // valor específico na tradução, quebrando pra todo mundo. É
                // por isso que a validação de contagem de placeholders em
                // AutoTranslationOrchestrator.Finish é essencial aqui.
                var candidates = selectedRows
                    .Select(r => AutoTranslationOrchestrator.BuildCandidate(r.Original, r.Original))
                    .ToList();

                var sourceLang = ModConfig.AutoTranslateSourceLang.Value;
                var targetLang = ModConfig.AutoTranslateTargetLang.Value;
                var token      = _autoCts.Token;

                var outcomes = await orchestrator.TranslateBatchAsync(
                    candidates, sourceLang, targetLang, token,
                    onProgress: (done, total) =>
                    {
                        UnityMainThreadDispatcher.Enqueue(() => panel.StatsLabel.text = $"Traduzindo... {done}/{total}");
                    }).ConfigureAwait(false);

                UnityMainThreadDispatcher.Enqueue(() => ShowAutoPreview(panel, selectedRows, outcomes));
            }
            catch (OperationCanceledException)
            {
                UnityMainThreadDispatcher.Enqueue(() => panel.StatsLabel.text = "Auto Tradução cancelada.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AQWTranslation] Auto Tradução falhou: {ex.Message}");
                UnityMainThreadDispatcher.Enqueue(() => panel.StatsLabel.text = $"Auto Tradução falhou: {ex.Message}");
            }
            finally
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    _autoBusy = false;
                    _autoCts?.Dispose();
                    _autoCts = null;
                });
            }
        }

        private void ShowAutoPreview(RowListPanel panel, List<EntryRow> selectedRows,
            List<AutoTranslationOrchestrator.TranslationOutcome> outcomes)
        {
            // modal singleton compartilhado entre janelas — guarda de qual
            // painel essas linhas vieram, pra ApplyAutoPreview saber onde
            // aplicar o resultado de volta
            _autoPreviewSourcePanel = panel;

            foreach (Transform t in _autoPreviewList) Destroy(t.gameObject);
            _autoPreviewRows.Clear();

            var byKey       = new Dictionary<string, EntryRow>(StringComparer.Ordinal);
            var savedByKey  = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in selectedRows) { byKey[r.Original] = r; savedByKey[r.Original] = r.Saved; }

            var plan = AutoPreviewPlanner.Plan(savedByKey, outcomes);
            foreach (var item in plan.Items)
                BuildAutoPreviewRow(byKey[item.Outcome.OriginalKey], item.Outcome, defaultChecked: item.DefaultChecked);

            _autoPreviewStats.text =
                $"{plan.Ok} pronta(s)  {plan.Suspicious} suspeita(s) (placeholder)  {plan.Failed} falharam" +
                $"  —  provedor: {_autoOrchestrator?.ProviderId}";

            _autoPreviewPanel.SetActive(true);
        }

        private void BuildAutoPreviewRow(EntryRow row,
            AutoTranslationOrchestrator.TranslationOutcome outcome, bool defaultChecked)
        {
            var alt  = _autoPreviewRows.Count % 2 == 1;
            var root = Go("PreviewRow", _autoPreviewList);
            Img(root, alt ? BgRowAlt : BgRow);
            // layoutPriority alto — mesmo empate de LayoutElement com
            // LayoutGroup/Image no mesmo GameObject, ver HRow() em UIHelpers.cs
            var rootLe = root.AddComponent<LayoutElement>();
            rootLe.layoutPriority  = 100;
            rootLe.preferredHeight = 46;
            var vl = root.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(6, 6, 2, 2); vl.spacing = 1;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            var topRow = Go("Top", root.transform);
            var topRowLe = topRow.AddComponent<LayoutElement>();
            topRowLe.layoutPriority  = 100;
            topRowLe.preferredHeight = 16;
            var thl = topRow.AddComponent<HorizontalLayoutGroup>();
            thl.spacing = 4; thl.childControlWidth = true; thl.childControlHeight = true;
            thl.childForceExpandWidth = false; thl.childForceExpandHeight = true;

            AutoPreviewRow pr = null!;
            var chkBtn = Btn(topRow, defaultChecked ? "[x]" : "[ ]", 30, 16,
                defaultChecked ? BgBtn : BgIgnBtn, () =>
                {
                    pr.Checked = !pr.Checked;
                    // CheckImg é o "Fill" do botão; o rótulo de texto é
                    // irmão dele, não filho, por isso sobe pro pai antes de
                    // procurar o Text (ver Btn() em UIHelpers.cs)
                    var t = pr.CheckImg.transform.parent.GetComponentInChildren<Text>();
                    if (t != null) t.text = pr.Checked ? "[x]" : "[ ]";
                    pr.CheckImg.color = pr.Checked ? BgBtn : BgIgnBtn;
                });

            Lbl(topRow, Shorten(row.Original, 46), 9, FontStyle.Normal, ColGray, 0, true);

            var color = outcome.PlaceholderMismatch ? ColMiss : ColOK;
            Lbl(root, Shorten(outcome.TranslatedText ?? "", 74), 10, FontStyle.Normal, color, 0, true);
            if (outcome.PlaceholderMismatch)
                Lbl(root, "⚠ contagem de {placeholder} mudou após a tradução — revise manualmente",
                    8, FontStyle.Italic, ColMiss, 0, true);

            pr = new AutoPreviewRow
            {
                OriginalKey  = row.Original,
                ProposedText = outcome.TranslatedText ?? "",
                SourceFile   = row.SourceFile,
                Suspicious   = outcome.PlaceholderMismatch,
                Checked      = defaultChecked,
                CheckImg     = chkBtn.GetComponentInChildren<Image>(),
                Root         = root,
            };

            // suspeita nunca é aplicável direto — desabilita o próprio
            // botão-checkbox em vez de deixar marcar e arriscar aplicar um
            // placeholder corrompido
            if (outcome.PlaceholderMismatch) chkBtn.interactable = false;

            _autoPreviewRows.Add(pr);
        }

        private void ApplyAutoPreview()
        {
            var panel = _autoPreviewSourcePanel;
            if (panel == null) { CloseAutoPreview(); return; }

            int applied = 0;
            foreach (var pr in _autoPreviewRows)
            {
                if (!pr.Checked || pr.Suspicious) continue;

                if (string.IsNullOrEmpty(pr.SourceFile))
                {
                    var dict = ReadFile();
                    dict[pr.OriginalKey] = pr.ProposedText;
                    WriteFile(dict);
                }
                else
                {
                    TranslationFileStore.Upsert(pr.SourceFile, pr.OriginalKey, pr.ProposedText, source: "auto");
                    TranslationManager.Instance.ReloadFile(pr.SourceFile);
                }

                // desmarca — já foi aplicada, não deve continuar marcada pro
                // próximo Auto Traduzir sem o usuário pedir de novo
                panel.Selection.Remove(pr.SourceFile, pr.OriginalKey);

                var row = panel.Rows.FirstOrDefault(r =>
                    r.Original == pr.OriginalKey && r.SourceFile == pr.SourceFile);
                if (row != null)
                {
                    row.TransFld.text      = pr.ProposedText;
                    row.Saved              = pr.ProposedText;
                    panel.Buffers[row.Original] = pr.ProposedText;
                    SetRowSelected(panel, row, false);
                    ApplyRowStatus(row);
                }

                applied++;
            }

            if (applied > 0)
            {
                TMPTextPatch.InvalidateSafeTranslateCache();
                UpdateSaveAllBtn(panel);
            }

            panel.StatsLabel.text = $"Auto Tradução: {applied} entrada(s) aplicada(s).";
            CloseAutoPreview();
        }

        private void CloseAutoPreview()
        {
            _autoCts?.Cancel();
            _autoPreviewPanel.SetActive(false);
            foreach (Transform t in _autoPreviewList) Destroy(t.gameObject);
            _autoPreviewRows.Clear();
            _autoPreviewSourcePanel = null;
        }

        private void BuildAutoPreviewPanel(RectTransform canvasRoot)
        {
            _autoPreviewPanel = Go("_AQWAutoPreviewPanel", canvasRoot);
            Img(_autoPreviewPanel, Bg);
            var wrt = _autoPreviewPanel.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(710, -510);
            wrt.sizeDelta        = new Vector2(500, 300);

            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _autoPreviewPanel.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(8, 8, 6, 6);
            vl.spacing              = 4;
            vl.childControlWidth    = true;
            vl.childControlHeight   = true;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            var hdr = HRow(_autoPreviewPanel, 26, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            Lbl(hdr, "Pré-visualização — Auto Tradução", 11, FontStyle.Bold, ColWhite, 0, true);
            Btn(hdr, "✕", 26, 22, BgClose, CloseAutoPreview);

            var statsRow = HRow(_autoPreviewPanel, 18, Bg);
            _autoPreviewStats = Lbl(statsRow, "", 9, FontStyle.Italic, ColGray, 0, true);

            _autoPreviewList = BuildScrollView(_autoPreviewPanel, 200);

            var foot = HRow(_autoPreviewPanel, 26, BgHdr);
            Btn(foot, "Aplicar marcadas", 130, 22, BgBtn, ApplyAutoPreview);
            Btn(foot, "Cancelar", 90, 22, BgClose, CloseAutoPreview);

            _autoPreviewPanel.SetActive(false);
        }

    }
}
