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
    // Construção de linha (BuildRow), chips de categoria, Salvar/Ignorar/Salvar tudo.
    public partial class TranslationEditorOverlay
    {
        // acima disto (em caracteres da chave normalizada), a linha não
        // oferece campo de edição inline, só "Trad. avançada" — comparado
        // contra `key`, que é o que aparece na linha
        private const int LongTextInlineThreshold = 80;

        private EntryRow BuildRow(RowListPanel panel, string key, string rawOrig, string saved, string cat,
                                   string sourcePath, string bufVal, bool alt, string sourceFile = "")
        {
            var root = Go("Row", panel.ListContent);
            Img(root, alt ? BgRowAlt : BgRow);
            // linha única: [] (texto original) (campo, quando cabível) [OK]
            // [Trad. avançada] [Del]. Construída com a janela já ativa, então
            // não sofre a corrida de rebuild que HRow() precisa contornar
            // pra cabeçalho — LayoutElement sozinho já basta aqui.
            var rowLe = root.AddComponent<LayoutElement>();
            rowLe.layoutPriority  = 100;
            rowLe.preferredHeight = 32;
            var hl = root.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(4, 4, 2, 2); hl.spacing = 4;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;

            var rowStatus = RowStatus.Compute(key, saved);
            Color sc = rowStatus.Kind switch
            {
                RowStatusKind.Translated => ColOK,
                RowStatusKind.Ignored    => ColIgnore,
                _                        => ColMiss,
            };

            EntryRow row = null!;

            // checkbox de seleção marca a linha como candidata ao Auto
            // Traduzir; estado inicial vem de panel.Selection (sobrevive a
            // Refresh/DoScan, que destroem e recriam EntryRow)
            bool initiallySelected = panel.Selection.Contains(sourceFile, key);
            var selBtn = Btn(root, initiallySelected ? "✓" : "", 28, 28,
                initiallySelected ? BgChipOn : BgChip, () => SetRowSelected(panel, row!, !row!.Selected));

            // quebra de linha vira espaço só na exibição do rótulo (a chave
            // em si não muda) — o Text é de uma linha só e cortaria o resto sem aviso
            var origLbl = Lbl(root, Shorten(key.Replace("\r", "").Replace('\n', ' '), 26),
                              10, FontStyle.Normal, sc, 184, false);
            origLbl.alignment = TextAnchor.MiddleLeft;

            // sem edição inline quando o texto original é muito longo ou tem
            // quebra de linha — só "Trad. avançada" (editor completo, área
            // multi-linha de verdade). Sem campo e sem "OK" nesse caso, já
            // que "OK" só existe pra salvar o que está no campo.
            bool tooLongForInline = key.Length > LongTextInlineThreshold
                                    || key.Contains('\n')
                                    || (rawOrig != null && rawOrig.Contains('\n'));

            // o campo (ou o aviso no lugar dele) e o "espaço do OK" são
            // flexíveis/fixos de forma que toda linha, com ou sem campo,
            // termine com os botões alinhados à direita na mesma posição
            InputField fld;
            if (!tooLongForInline)
            {
                fld = Fld(root, 0, 22, "traducao...", flex: true);
            }
            else
            {
                // o InputField ainda precisa existir (oculto, fora do
                // HorizontalLayoutGroup) porque TransFld é a fonte da verdade
                // que SaveEntry/SaveAll/RefreshVisibility/AutoTranslate/
                // MoveCopy/Detail leem e escrevem — tornar nullable tocaria
                // muitos call sites só por causa de um caso visual
                fld = Fld(root, 0, 22, "traducao...", flex: true);
                fld.transform.parent.gameObject.SetActive(false);

                var notice = Lbl(root, "Texto muito grande. Traduza com a tradução avançada",
                                 9, FontStyle.Italic, ColGray, 0, true);
                notice.alignment = TextAnchor.MiddleLeft;
            }
            fld.text = bufVal;

            Button? okBtn = null;
            if (!tooLongForInline)
            {
                okBtn = Btn(root, "OK", 32, 22, BgBtn, () =>
                    SaveEntryRequiringDestination(row!, () => UpdateSaveAllBtn(panel)));
            }
            else
            {
                // reserva o lugar do "OK" (mesma largura) pra "Trad. avançada"
                // e "Del" não deslizarem pra esquerda nessas linhas
                var spacer = Go("OkSpacer", root.transform);
                var sle = spacer.AddComponent<LayoutElement>();
                sle.preferredWidth = 32; sle.flexibleWidth = 0;
            }

            var detBtn = Btn(root, "Trad. avançada", 96, 22, BgIgnBtn, () => OpenDetail(panel, row!));

            // Del só pra tradução já inserida num arquivo real (sourceFile
            // preenchido) — não faz sentido em "Textos detectados na tela",
            // uma linha do Scan não é uma entrada salva que possa ser
            // apagada, só texto capturado da tela. `sourceFile` aqui é o
            // parâmetro, não `row.SourceFile` (que só muda depois de um "OK"
            // bem-sucedido — nesse caso a linha some do Scan no próximo
            // refresh de qualquer jeito, já traduzida).
            if (!string.IsNullOrEmpty(sourceFile))
            {
                Btn(root, "Del", 30, 22, BgClose, () =>
                {
                    TranslationFileStore.Remove(row!.SourceFile, row!.Original);
                    TranslationManager.Instance.ReloadFile(row!.SourceFile);
                    TMPTextPatch.InvalidateSafeTranslateCache();
                    panel.Buffers.Remove(row!.Original);

                    // remoção explícita do registry, único caminho que tira
                    // algo dele além do botão Limpar
                    CaptureRegistry.Remove(row!.Original);
                    panel.Selection.Remove(row!.SourceFile, row!.Original);
                    Destroy(row!.Root);
                    panel.Rows.Remove(row!);
                    UpdateSaveAllBtn(panel);
                });
            }

            row = new EntryRow
            {
                Original    = key,
                RawOriginal = rawOrig,
                Saved       = saved,
                Category    = cat,
                SourcePath  = sourcePath,
                SourceFile  = sourceFile,
                Selected    = initiallySelected,
                SelImg      = selBtn.GetComponentInChildren<Image>(),
                Alt         = alt,
                RootImg     = root.GetComponent<Image>(),
                Root        = root,
                OrigLbl     = origLbl,
                TransFld    = fld,
                OkBtn       = okBtn!,
            };

            if (rowStatus.Kind == RowStatusKind.Ignored) root.GetComponent<Image>().color = BgIgnoredRow;
            fld.onValueChanged.AddListener(_ => UpdateSaveAllBtn(panel));
            return row;
        }

        // o seletor de arquivo devolve caminho relativo a translations/
        // ("maps/pirates.json"), mas EntryRow.SourceFile/TranslationFileStore/
        // ReloadFile trabalham com caminho completo — usar o relativo direto
        // resolveria contra o diretório de trabalho do jogo, gravando fora de
        // BepInEx/plugins/translations (nunca carregado, nunca aparece em "Abrir")
        private static string ResolveCorpusFullPath(string relativePath) =>
            Path.Combine(TranslationManager.Instance.TranslationsRootPath,
                         relativePath.Replace('/', Path.DirectorySeparatorChar));

        private void SaveEntry(EntryRow row)
        {
            if (!row.IsModified) return;
            SaveEntryWithText(row, row.TransFld.text);
        }

        // texto capturado ANTES de abrir o seletor de arquivo, não relido
        // depois: SaveEntryRequiringDestination costumava chamar SaveEntry(row)
        // de dentro do callback do seletor, que só roda depois de o usuário
        // navegar a árvore e clicar "Selecionar" — um tempo real. Nesse
        // meio-tempo a janela de Scan se auto-atualiza sozinha
        // (QuickCheckAndRescanScanWindow) e destrói/recria todo EntryRow, e
        // `row` virava um objeto morto (GameObject destruído, referência C#
        // ainda válida) — ler `row.TransFld.text` naquele momento podia
        // voltar vazio e `if (!row.IsModified)` abortava o save inteiro sem
        // erro nenhum visível. QuickCheckAndRescanScanWindow também pausa
        // enquanto o seletor está aberto, mas capturar o texto antes é o que
        // garante correção mesmo que outro caminho de refresh apareça no futuro.
        private void SaveEntryWithText(EntryRow row, string text)
        {
            TranslationFileStore.Upsert(row.SourceFile, row.Original, text);
            TranslationManager.Instance.ReloadFile(row.SourceFile);
            TMPTextPatch.InvalidateSafeTranslateCache();

            row.Saved              = text;
            _buffers[row.Original] = text;
            // Unity: comparar Component com null detecta corretamente um
            // GameObject já destruído — se o row morreu no meio do caminho,
            // a gravação em disco já aconteceu, só o refresh visual é pulado
            if (row.Root != null) ApplyRowStatus(row);
        }

        // "OK" passa por aqui em vez de chamar SaveEntry direto: linha do
        // Scan (SourceFile="") nunca mais cai direto em uncategorized.json,
        // primeiro obriga escolher um arquivo real via OpenFilePicker. Linha
        // que já tem SourceFile (Biblioteca/"Abrir") salva direto, sem
        // perguntar — já está categorizada. `onSaved` é só o refresh visual
        // leve (UpdateSaveAllBtn) que cada chamador já fazia antes.
        private void SaveEntryRequiringDestination(EntryRow row, Action onSaved)
        {
            if (!row.IsModified) return;
            var text = row.TransFld.text; // capturado agora, ver nota em SaveEntryWithText

            if (string.IsNullOrEmpty(row.SourceFile))
            {
                OpenFilePicker($"Onde salvar \"{Shorten(row.Original, 40)}\"?", relPath =>
                {
                    row.SourceFile = ResolveCorpusFullPath(relPath);
                    SaveEntryWithText(row, text);
                    onSaved();
                });
                return;
            }

            SaveEntryWithText(row, text);
            onSaved();
        }

        private void SaveAll(RowListPanel panel)
        {
            var modified = panel.Rows.Where(r => r.IsModified).ToList();
            if (modified.Count == 0) return;

            var toIngameEdits = modified.Where(r => string.IsNullOrEmpty(r.SourceFile)).ToList();
            if (toIngameEdits.Count > 0)
            {
                var dict = ReadFile();
                foreach (var row in toIngameEdits)
                    dict[row.Original] = row.TransFld.text;
                WriteFile(dict);
            }

            var toFiles = modified.Where(r => !string.IsNullOrEmpty(r.SourceFile)).ToList();
            foreach (var row in toFiles)
            {
                TranslationFileStore.Upsert(row.SourceFile, row.Original, row.TransFld.text);
                TranslationManager.Instance.ReloadFile(row.SourceFile);
            }
            if (toFiles.Count > 0)
                TMPTextPatch.InvalidateSafeTranslateCache();

            foreach (var row in modified)
            {
                row.Saved              = row.TransFld.text;
                panel.Buffers[row.Original] = row.TransFld.text;
                ApplyRowStatus(row);
            }
            UpdateSaveAllBtn(panel);
        }

        private static void ApplyRowStatus(EntryRow row)
        {
            var status = RowStatus.Compute(row.Original, row.Saved);
            Color c = status.Kind switch
            {
                RowStatusKind.Translated => ColOK,
                RowStatusKind.Ignored    => ColIgnore,
                _                        => ColMiss,
            };
            row.OrigLbl.color   = c;
            if (row.RootImg != null)
                row.RootImg.color = status.Kind == RowStatusKind.Ignored ? BgIgnoredRow : (row.Alt ? BgRowAlt : BgRow);
        }

    }
}
