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
    // Modal de detalhe ("editor avançado") — texto completo, placeholders, Auto Traduzir de uma linha.
    public partial class TranslationEditorOverlay
    {
        private void BuildDetailPanel(RectTransform canvasRoot)
        {
            _detailPanel = Go("_AQWDetailPanel", canvasRoot);
            Img(_detailPanel, Bg);
            var wrt = _detailPanel.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(710, -20);
            wrt.sizeDelta        = new Vector2(500, 478);

            // sibling fora do RectTransform da janela principal — sem isto,
            // um clique fora da área da janela principal vazaria pro jogo por baixo
            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _detailPanel.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(8, 8, 6, 6);
            vl.spacing              = 4;
            vl.childControlWidth    = true;
            vl.childControlHeight   = true;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            // Header
            var hdr = HRow(_detailPanel, 28, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            Lbl(hdr, "Detalhe / Edicao", 12, FontStyle.Bold, ColWhite, 0, true);
            Btn(hdr, "✕", 28, 24, BgClose, CloseDetail);

            // Original label header + copy button
            var oh = HRow(_detailPanel, 18, BgHdr);
            Lbl(oh, "Original (EN) — selecione e copie:", 10, FontStyle.Bold, ColGray, 0, true);
            Btn(oh, "Copiar tudo", 76, 16, BgIgnBtn,
                () => { if (_detailOrigFld != null) GUIUtility.systemCopyBuffer = _detailOrigFld.text; });

            // Original InputField — multiline, selecionável (usuario pode marcar e copiar tags)
            var origArea = Go("OrigArea", _detailPanel.transform);
            Img(origArea, BgInput);
            origArea.AddComponent<LayoutElement>().preferredHeight = 110;

            var oTgo = Go("T", origArea.transform);
            var oTrt = oTgo.GetComponent<RectTransform>();
            oTrt.anchorMin = Vector2.zero; oTrt.anchorMax = Vector2.one;
            oTrt.offsetMin = new Vector2(6, 4); oTrt.offsetMax = new Vector2(-6, -4);
            var oTt = oTgo.AddComponent<Text>();
            oTt.font = GetFont(); oTt.fontSize = 11; oTt.color = ColWhite;
            oTt.supportRichText = false; oTt.alignment = TextAnchor.UpperLeft;
            oTt.horizontalOverflow = HorizontalWrapMode.Wrap;
            oTt.verticalOverflow   = VerticalWrapMode.Overflow;

            _detailOrigFld = origArea.AddComponent<InputField>();
            _detailOrigFld.lineType      = InputField.LineType.MultiLineNewline;
            _detailOrigFld.targetGraphic = origArea.GetComponent<Image>();
            _detailOrigFld.textComponent = oTt;
            // só leitura — o campo existe pra selecionar/copiar o original,
            // nunca pra editar (readOnly mantém clique/seleção/Ctrl+C funcionando)
            _detailOrigFld.readOnly      = true;

            // Hint label
            var hintRow = HRow(_detailPanel, 16, Bg);
            Lbl(hintRow, "Placeholders:  {player} = username   {n1},{n2},… = numeros",
                9, FontStyle.Italic, ColGray, 0, true);

            // Translation label
            var th = HRow(_detailPanel, 18, BgHdr);
            Lbl(th, "Traducao PT-BR:", 10, FontStyle.Bold, ColGray, 0, true);

            // botões de placeholder inserem o token no cursor, evitando
            // digitar os "códigos" à mão — ENTER já quebra a linha (o \n é
            // gerenciado automaticamente no load/save), sem botão pra isso
            var tokRow = HRow(_detailPanel, 22, Bg);
            Lbl(tokRow, "Inserir:", 9, FontStyle.Italic, ColGray, 46, false);
            foreach (var tok in new[] { "{player}", "{n1}", "{n2}", "{n1}/{n2}", "{n3}", "{map}", "{quest}" })
            {
                var t = tok; // captura por valor para o closure
                var w = t.Length > 6 ? 74 : 56; // "{n1}/{n2}" é mais largo que os outros
                Btn(tokRow, t, w, 18, BgIgnBtn, () => InsertToken(t));
            }

            // Translation InputField (multiline)
            var transArea = Go("TransArea", _detailPanel.transform);
            Img(transArea, BgInput);
            transArea.AddComponent<LayoutElement>().preferredHeight = 160;

            _detailTransFld = transArea.AddComponent<InputField>();
            _detailTransFld.lineType = InputField.LineType.MultiLineNewline;

            var tph = Go("PH", transArea.transform);
            var tphrt = tph.GetComponent<RectTransform>();
            tphrt.anchorMin = Vector2.zero; tphrt.anchorMax = Vector2.one;
            tphrt.offsetMin = new Vector2(6, 4); tphrt.offsetMax = new Vector2(-6, -4);
            var tpht = tph.AddComponent<Text>();
            tpht.font = GetFont(); tpht.text = "Digite a traducao... Use {player} e {n1}";
            tpht.fontSize = 10; tpht.color = ColGray; tpht.fontStyle = FontStyle.Italic;
            tpht.alignment = TextAnchor.UpperLeft;

            var tgo = Go("T", transArea.transform);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6, 4); trt.offsetMax = new Vector2(-6, -4);
            var tt = tgo.AddComponent<Text>();
            tt.font = GetFont(); tt.fontSize = 11; tt.color = ColWhite;
            tt.supportRichText = false; tt.alignment = TextAnchor.UpperLeft;
            tt.horizontalOverflow = HorizontalWrapMode.Wrap;
            tt.verticalOverflow   = VerticalWrapMode.Overflow;

            _detailTransFld.targetGraphic = transArea.GetComponent<Image>();
            _detailTransFld.placeholder   = tpht;
            _detailTransFld.textComponent = tt;

            // Footer
            var foot = HRow(_detailPanel, 30, BgHdr);
            Btn(foot, "Salvar",     80, 24, BgBtn,    SaveFromDetail);
            var detailAutoBtn = Btn(foot, "Auto Traduzir", 104, 24, BgIgnBtn, AutoTranslateFromDetail);
            _detailAutoBtnText = detailAutoBtn.GetComponentInChildren<Text>();
            Btn(foot, "Fechar",     80, 24, BgClose,  CloseDetail);

            _detailPanel.SetActive(false);
        }

        private void OpenDetail(RowListPanel panel, EntryRow row)
        {
            _detailSourcePanel = panel;
            _detailRow = row;
            _detailOrigFld.text  = string.IsNullOrEmpty(row.RawOriginal)
                                   ? row.Original
                                   : row.RawOriginal;
            // Mostra a quebra de linha REAL no campo (converte o "\n" literal
            // armazenado), para o tradutor não ver nem digitar "\n".
            _detailTransFld.text = ToEditable(row.TransFld.text);
            _lastTransCaret      = _detailTransFld.text.Length;
            // fica acima das janelas de arquivo e do Scan (criadas depois
            // deste painel, por isso desenhavam por cima) — abaixo só de Mover/seletor
            _detailPanel.transform.SetAsLastSibling();
            if (_movePanel != null && _movePanel.activeSelf) _movePanel.transform.SetAsLastSibling();
            _detailPanel.SetActive(true);
        }

        private void CloseDetail()
        {
            // sincroniza de volta o texto digitado sem salvar (reconverte
            // quebra real -> "\n" literal, formato de armazenamento)
            if (_detailRow != null)
                _detailRow.TransFld.text = FromEditable(_detailTransFld.text);
            _detailPanel.SetActive(false);
            _detailRow = null;
        }

        private void SaveFromDetail()
        {
            if (_detailRow == null) return;
            // o campo tem quebra real (ENTER); converte pra "\n" literal
            // antes de gravar, assim o JSON guarda o token e o jogo renderiza a quebra
            var text = FromEditable(_detailTransFld.text);
            if (string.IsNullOrWhiteSpace(text)) return;

            // salva direto, sem checar IsModified — o usuário clicou "Salvar" explicitamente
            _detailRow.TransFld.text = text;

            // mesma regra do "OK" da linha (SaveEntryRequiringDestination):
            // linha do Scan nunca cai direto em uncategorized.json, precisa
            // escolher um arquivo primeiro. "Trad. avançada" é o único jeito
            // de salvar texto muito longo (o "OK" inline some nesse caso),
            // então esta gate precisa existir aqui também.
            if (string.IsNullOrEmpty(_detailRow.SourceFile))
            {
                var row = _detailRow;
                OpenFilePicker($"Onde salvar \"{Shorten(row.Original, 40)}\"?", relPath =>
                {
                    row.SourceFile = ResolveCorpusFullPath(relPath);
                    FinishSaveFromDetail(row, text);
                });
                return;
            }

            FinishSaveFromDetail(_detailRow, text);
        }

        private void FinishSaveFromDetail(EntryRow row, string text)
        {
            TranslationFileStore.Upsert(row.SourceFile, row.Original, text);
            TranslationManager.Instance.ReloadFile(row.SourceFile);
            TMPTextPatch.InvalidateSafeTranslateCache();

            row.Saved              = text;
            _buffers[row.Original] = text;
            // mesma proteção de Rows.cs/SaveEntryWithText — a linha pode ter
            // sido destruída por um rescan enquanto o seletor estava aberto;
            // a gravação em disco acima já aconteceu de qualquer forma
            if (row.Root != null) ApplyRowStatus(row);
            if (_detailSourcePanel != null) UpdateSaveAllBtn(_detailSourcePanel);
        }

        // traduz só a entrada aberta no momento e preenche o campo — não
        // salva sozinho, precisa clicar "Salvar" igual qualquer edição manual
        private void AutoTranslateFromDetail()
        {
            if (_detailRow == null) return;
            if (_autoBusy)
            {
                _statsLabel.text = "Já existe uma tradução automática em andamento.";
                return;
            }
            if (!ModConfig.AutoTranslateEnabled.Value)
            {
                _statsLabel.text = "Auto Tradução desativada em Configurações.";
                return;
            }

            _ = RunAutoTranslateFromDetailAsync(_detailRow);
        }

        private async Task RunAutoTranslateFromDetailAsync(EntryRow row)
        {
            _autoBusy = true;
            if (_detailAutoBtnText != null) _detailAutoBtnText.text = "Traduzindo...";

            try
            {
                var orchestrator = GetOrCreateOrchestrator();
                // usa row.Original (chave já normalizada, com {n1}/{n2} se
                // aplicável), nunca o texto bruto do campo "Original (EN)" —
                // o texto bruto pode ter um valor real capturado (username
                // específico, número cru) que iria parar hardcoded na tradução salva
                var candidate = AutoTranslationOrchestrator.BuildCandidate(row.Original, row.Original);

                var sourceLang = ModConfig.AutoTranslateSourceLang.Value;
                var targetLang = ModConfig.AutoTranslateTargetLang.Value;

                var outcomes = await orchestrator.TranslateBatchAsync(
                    new List<AutoTranslationOrchestrator.Candidate> { candidate },
                    sourceLang, targetLang, CancellationToken.None).ConfigureAwait(false);

                var outcome = outcomes.Count > 0 ? outcomes[0] : null;
                UnityMainThreadDispatcher.Enqueue(() => ApplyDetailAutoTranslateResult(row, outcome));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AQWTranslation] Auto Tradução (detalhe) falhou: {ex.Message}");
                UnityMainThreadDispatcher.Enqueue(() => _statsLabel.text = $"Auto Tradução falhou: {ex.Message}");
            }
            finally
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    _autoBusy = false;
                    if (_detailAutoBtnText != null) _detailAutoBtnText.text = "Auto Traduzir";
                });
            }
        }

        private void ApplyDetailAutoTranslateResult(EntryRow row, AutoTranslationOrchestrator.TranslationOutcome? outcome)
        {
            // se o usuário fechou o modal ou abriu outra linha enquanto a
            // tradução rodava, não aplica em cima do que está aberto agora
            if (_detailRow != row || _detailTransFld == null) return;

            if (outcome == null || !outcome.Success)
            {
                _statsLabel.text = $"Auto Tradução falhou: {outcome?.Error ?? "erro desconhecido"}";
                return;
            }

            _detailTransFld.text = ToEditable(outcome.TranslatedText ?? "");
            _statsLabel.text = outcome.PlaceholderMismatch
                ? "Auto Tradução aplicada, mas a contagem de {placeholder} mudou — revise antes de salvar."
                : "Auto Tradução aplicada ao campo — clique Salvar para confirmar.";
        }

        // conversão \n literal <-> quebra real, só pro campo de edição: o
        // armazenamento (dict/TransFld/JSON) sempre usa "\n" literal (2
        // chars); o campo usa quebra real, pro tradutor não ver nem digitar
        // "\n" — ENTER quebra a linha e o save reconverte
        private static string ToEditable(string s)
            => string.IsNullOrEmpty(s) ? s
               : s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");

        private static string FromEditable(string s)
            => string.IsNullOrEmpty(s) ? s
               : s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        // insere o token na posição do cursor e deixa o cursor logo depois
        // dele, sem nada selecionado. A correção do caret é feita via patch
        // Harmony direto no InputField (InputFieldCaretFix.cs) — polling num
        // Update/LateUpdate nosso não seria confiável porque
        // UnityEngine.UI.InputField tem seu próprio LateUpdate(), e a ordem
        // entre LateUpdate()s de componentes diferentes não é garantida.
        private void InsertToken(string token)
        {
            if (_detailTransFld == null) return;
            var cur = _detailTransFld.text ?? string.Empty;
            int caret = Mathf.Clamp(_lastTransCaret, 0, cur.Length);
            var next  = cur.Substring(0, caret) + token + cur.Substring(caret);
            _detailTransFld.text = next;
            int newCaret = caret + token.Length;

            _detailTransFld.ActivateInputField();
            InputFieldCaretFix.RequestCaret(_detailTransFld, newCaret);
            _lastTransCaret = newCaret;
        }

    }
}
