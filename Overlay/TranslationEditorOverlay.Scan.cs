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
    // Varredura (Scan): captura de TMP_Text/UI.Text visíveis, e a
    // construção da lista/filtros do modo Scan. Ver TranslationEditorOverlay.cs
    // (arquivo núcleo desta mesma partial class) para campos/tipos.
    public partial class TranslationEditorOverlay
    {
        /// <summary>
        /// Varredura ativa: itera todo componente de texto visível do jogo —
        /// TMP_Text e UnityEngine.UI.Text legado — e registra cada um no
        /// CaptureRegistry, carimbando LastSeen=now. É o que faz texto
        /// in-game aparecer mesmo sem patch Harmony: o jogo pode usar UI.Text
        /// legado em balão de fala/diálogo, que os patches TMP nunca tocam.
        /// FindObjectsInactive.Include cobre objeto inativo já exibido antes
        /// (diálogo dispensado, popup fechado, cutscene).
        /// </summary>
        private void ScanVisibleIntoRegistry()
        {
            float now = Time.realtimeSinceStartup;
            bool  verbose = ModConfig.VerboseLogging?.Value == true;
            int   tmpSeen = 0, tmpKept = 0;
            // contadores de rejeição, logados sempre (não só verbose) pra
            // diagnosticar por que texto in-game não chega ao registry
            int rejDisabled = 0, rejAqw = 0, rejInactiveNoOrig = 0,
                rejInputEdit = 0, rejEmpty = 0, rejErr = 0;
            int regBefore = CaptureRegistry.Count;
            var roots = new HashSet<string>(StringComparer.Ordinal); // roots distintos, revela quais canvases existem in-game
            var samples = new List<string>(); // amostra de texto bruto encontrado, pra diagnóstico

            // cada item em try/catch: um único objeto problemático do jogo
            // (ex.: texto de HUD que dispara exceção numa normalização) nunca
            // pode abortar a varredura inteira e zerar a lista
            var allTmp = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var tmp in allTmp)
            {
                tmpSeen++;
                try
                {
                    var rootName = tmp.transform.root.name ?? "?";
                    if (roots.Count < 40) roots.Add(rootName);
                    // qualquer TMP ativo com texto não-vazio, até 20 amostras
                    if (samples.Count < 20 && tmp.isActiveAndEnabled)
                    {
                        var st = tmp.text ?? "";
                        if (st.Length > 50) st = st.Substring(0, 50);
                        st = st.Replace('\n', ' ').Replace('\r', ' ').Trim();
                        if (st.Length >= 2) samples.Add($"[{rootName}] {st}");
                    }
                    if (!tmp.enabled) { rejDisabled++; continue; }
                    if (rootName.StartsWith("_AQW")) { rejAqw++; continue; }

                    // inativo só entra se o Harmony já processou (tem original)
                    bool hasOriginal = TMPTextPatch.TryGetValidOriginal(tmp, out var oe);
                    if (!tmp.isActiveAndEnabled && !hasOriginal) { rejInactiveNoOrig++; continue; }

                    // pula só InputField que o usuário está realmente editando
                    // agora (chat, login, busca): focado + editável + interativo.
                    // Diálogo in-game usa um TMP_InputField como container de
                    // texto ("Box > Text (TMP)"), normalmente readOnly/sem
                    // foco — esse texto é narrativo e deve ser capturado.
                    {
                        var pif = tmp.GetComponentInParent<TMP_InputField>();
                        if (pif != null && pif.isFocused && !pif.readOnly && pif.interactable)
                        {
                            rejInputEdit++;
                            if (verbose)
                            {
                                var s = tmp.text ?? string.Empty;
                                if (s.Length > 40) s = s.Substring(0, 40);
                                Plugin.Log.LogInfo("[Scan] skip(InputField em edicao): " + s);
                            }
                            continue;
                        }
                    }

                    string rawText = hasOriginal ? oe.Value.Trim() : (tmp.text?.Trim() ?? "");
                    string cat     = hasOriginal ? oe.Category     : TMPTextPatch.GetCategory(tmp.transform);
                    if (string.IsNullOrWhiteSpace(rawText)) { rejEmpty++; continue; }
                    RecordVisible(rawText, cat, tmp.transform, tmp.GetType().Name, now, "scan");
                    tmpKept++;

                    // enforcement-on-scan: alguns textos do jogo são definidos
                    // por um caminho de escrita que os patches Harmony não
                    // interceptam (set_text/SetText/OnEnable) — a captura os
                    // encontra, mas continuam em inglês na tela (ex.: popup
                    // 'Goto map ... quest "..."'). Reaplica a tradução sobre
                    // rawText (o original já resolvido acima), nunca sobre
                    // `tmp.text` bruto — se usasse tmp.text, depois da
                    // primeira reescrita pra PT-BR a passada seguinte leria o
                    // próprio PT-BR como "original", falharia o lookup e
                    // registraria esse texto PT-BR no CaptureRegistry como se
                    // fosse um original sem tradução (bug real, corrigido).
                    // `rawText` resolve pro original em inglês via
                    // OriginalTracker exatamente pra evitar isso.
                    if (ModConfig.EnforceOnScan?.Value == true && tmp.isActiveAndEnabled)
                    {
                        try
                        {
                            if (rawText.Length >= 2)
                            {
                                var tr = TMPTextPatch.SafeTranslate(rawText, tmp);
                                if (tr != rawText && tr != tmp.text)
                                    tmp.text = tr;
                            }
                        }
                        catch { /* enforcement best-effort: nunca derruba o scan */ }
                    }
                }
                catch (Exception ex)
                {
                    rejErr++;
                    if (verbose) Plugin.Log.LogWarning($"[Scan] TMP ignorado por erro: {ex.Message}");
                }
            }

            // log throttled: a captura roda a cada ~1s, só emite quando algo
            // muda ou a cada 5s, pra não inundar o console
            int regAfter = CaptureRegistry.Count;
            bool changed = allTmp.Length != _lastScanFind
                           || tmpKept != _lastScanKept
                           || regAfter != _lastScanReg;
            if (changed || now - _lastScanLogTime >= 5f)
            {
                _lastScanFind = allTmp.Length; _lastScanKept = tmpKept; _lastScanReg = regAfter;
                _lastScanLogTime = now;
                Plugin.Log.LogInfo(
                    $"[Scan] TMP find={allTmp.Length} visto={tmpSeen} mantido={tmpKept}" +
                    $" | rej: disabled={rejDisabled} aqw={rejAqw} inativo={rejInactiveNoOrig}" +
                    $" inputEdit={rejInputEdit} vazio={rejEmpty} erro={rejErr}" +
                    $" | registry {regBefore}->{regAfter}" +
                    $" | roots=[{string.Join(", ", roots)}]");
            }

            // UnityEngine.UI.Text legado não é patcheado pelo Harmony — balão
            // de fala/diálogo in-game do AQW usa UI.Text com frequência, sem
            // esta varredura esse texto seria invisível ao mod
            var allLeg = FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int legKept = 0;
            foreach (var leg in allLeg)
            {
                try
                {
                    if (!leg.enabled || !leg.isActiveAndEnabled) continue;
                    if (leg.transform.root.name?.StartsWith("_AQW") == true) continue; // ignora a própria overlay
                    { var pif = leg.GetComponentInParent<InputField>(); if (pif != null) continue; } // texto de InputField legado é conteúdo do usuário

                    // se o enforcement já traduziu este UI.Text, leg.text está
                    // em PT — pra captura precisamos do original EN guardado.
                    // Detecção de reuso: se o texto visível ainda é exatamente
                    // o que escrevemos (loe.Translated), o original é o
                    // rastreado; se o jogo trocou o texto (componente
                    // reaproveitado pra outra fala), o visível vira o novo
                    // original e descarta o rastreamento.
                    string liveText = leg.text?.Trim() ?? "";
                    bool legHasOriginal =
                        TMPTextPatch.LegacyOriginalTracker.TryGetValue(leg, out var loe)
                        && !string.IsNullOrWhiteSpace(loe.Value)
                        && liveText == loe.Translated;
                    var rawText = legHasOriginal ? loe.Value.Trim() : liveText;
                    var cat     = legHasOriginal ? loe.Category     : TMPTextPatch.GetCategory(leg.transform);
                    RecordVisible(rawText, cat, leg.transform, "UI.Text", now, "ui");
                    legKept++;

                    // patches Harmony são só em TMP_Text, então UI.Text nunca
                    // é traduzido na escrita — reaplica aqui sobre o original
                    // EN. Idempotente: se já traduzido, TranslateRaw devolve
                    // o mesmo e não reescreve.
                    if (ModConfig.EnforceOnScan?.Value == true)
                    {
                        try
                        {
                            var tr = TMPTextPatch.TranslateRaw(rawText, leg.transform);
                            if (tr != rawText && tr != leg.text)
                            {
                                var ne      = TMPTextPatch.LegacyOriginalTracker.GetOrCreateValue(leg);
                                ne.Value    = rawText;
                                ne.Category = TMPTextPatch.GetCategory(leg.transform);
                                ne.Translated = tr;
                                leg.text    = tr;
                            }
                        }
                        catch { /* enforcement best-effort */ }
                    }
                }
                catch (Exception ex)
                {
                    if (verbose) Plugin.Log.LogWarning($"[Scan] UI.Text ignorado por erro: {ex.Message}");
                }
            }

            // dump de diagnóstico pra arquivo, sobrescreve a cada scan.
            // Gated por VerboseLogging — sem isso este I/O de disco rodaria a
            // cada ~1s indefinidamente mesmo com ninguém olhando o arquivo.
            if (verbose)
            {
                try
                {
                    var dbg =
                        $"t={now:0.0}  frame={Time.frameCount}\n" +
                        $"TMP  find={allTmp.Length} mantido={tmpKept}  rej[disabled={rejDisabled} aqw={rejAqw} inativo={rejInactiveNoOrig} inputEdit={rejInputEdit} vazio={rejEmpty} erro={rejErr}]\n" +
                        $"UI.Text find={allLeg.Length} mantido={legKept}\n" +
                        $"registry {regBefore}->{CaptureRegistry.Count}\n" +
                        $"roots=[{string.Join(", ", roots)}]\n" +
                        $"amostras:\n  " + string.Join("\n  ", samples);
                    File.WriteAllText(
                        Path.Combine(TranslationManager.Instance.TranslationsRootPath, "_scan_debug.txt"),
                        dbg);
                }
                catch { /* diagnóstico best-effort */ }
            }
        }

        /// <summary>
        /// Aplica os mesmos filtros/normalização do DoTranslate a um texto bruto e,
        /// se sobreviver, registra no CaptureRegistry. Compartilhado por TMP e UI.Text.
        /// </summary>
        private static void RecordVisible(
            string rawText, string cat, Transform t, string typeName, float now, string source)
        {
            if (string.IsNullOrWhiteSpace(rawText) || rawText.Length < 2) return;

            var stripped = rawText.Contains('<') ? TMPTextPatch.StripRichTextTags(rawText) : rawText;
            stripped = TMPTextPatch.StripDialogCursor(stripped);
            if (string.IsNullOrWhiteSpace(stripped) || stripped.Length < 2) return;
            if (TMPTextPatch.IsNumericOrDate(stripped)) return;
            if (TMPTextPatch.IsDynamicGameContent(stripped)) return;
            if (string.Equals(t.parent?.name, "Names", StringComparison.Ordinal)) return;

            // mesma normalização do push do template de quest, pra lista
            // mostrar a chave (e o nome da quest sozinho) em vez do literal
            if (TMPTextPatch.TryNormalizeQuestGoto(stripped, out var qKey, out var qMap, out var qQuest))
            {
                CaptureRegistry.Record(qKey, rawText, cat, PathOf(t, typeName),
                    null, null, new[] { qMap }, now, source);
                if (qQuest.Length >= 2)
                    CaptureRegistry.Record(qQuest, qQuest, cat, PathOf(t, typeName),
                        null, null, null, now, source);
                return;
            }

            var step1 = TMPTextPatch.NormalizeUsernamePlaceholders(stripped, out var players);
            var step2 = TMPTextPatch.NormalizeNumbers(step1, out var nums);
            var key   = TMPTextPatch.NormalizeMaps(step2, out var maps);
            if (string.IsNullOrWhiteSpace(key) || key.Length < 2) return;

            CaptureRegistry.Record(key, rawText, cat, PathOf(t, typeName),
                players, nums, maps, now, source);
        }

        /// <summary>
        /// Conjunto de chaves VISÍVEIS agora (TMP + UI.Text ativos). Usado como
        /// baseline do auto-refresh (Trigger 2) e para preencher _lastSeenTexts.
        /// </summary>
        private HashSet<string> CollectVisibleTextSet()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            // cada elemento em try/catch: alguns TMP de HUD (ex.: contador "1
            // player in infinityportal-9") lançam exceção interna do Unity/TMP
            // ("Value must be positive") só de ler .text — sem esse guard o
            // throw subia até o try do DoScan e abortava com a lista vazia,
            // por isso in-game a lista nunca populava, mas na tela de login
            // (sem esse HUD) completava normal
            foreach (var tmp in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                try
                {
                    if (!tmp.isActiveAndEnabled) continue;
                    { var pif2 = tmp.GetComponentInParent<TMP_InputField>(); if (pif2 != null && !pif2.readOnly) continue; }
                    if (tmp.transform.root.name?.StartsWith("_AQW") == true) continue;

                    string raw;
                    if (TMPTextPatch.TryGetValidOriginal(tmp, out var e))
                        raw = e.Value.Trim();
                    else
                        raw = tmp.text?.Trim() ?? "";

                    AddVisibleKey(set, raw, tmp.transform);
                }
                catch { /* elemento problemático do jogo — ignora, nunca aborta */ }
            }

            foreach (var leg in FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
            {
                try
                {
                    if (!leg.isActiveAndEnabled) continue;
                    if (leg.transform.root.name?.StartsWith("_AQW") == true) continue;
                    { var pif = leg.GetComponentInParent<InputField>(); if (pif != null) continue; }
                    AddVisibleKey(set, leg.text?.Trim() ?? "", leg.transform);
                }
                catch { /* idem */ }
            }

            return set;
        }

        private void AddVisibleKey(HashSet<string> set, string raw, Transform t)
        {
            if (set.Count >= MaxEntries) return;
            var stripped = raw.Contains('<') ? TMPTextPatch.StripRichTextTags(raw) : raw;
            stripped = TMPTextPatch.StripDialogCursor(stripped);
            if (string.IsNullOrWhiteSpace(stripped) || stripped.Length < 2) return;
            if (TMPTextPatch.IsNumericOrDate(stripped)) return;
            if (TMPTextPatch.IsDynamicGameContent(stripped)) return;
            if (string.Equals(t.parent?.name, "Names", StringComparison.Ordinal)) return;

            var step1 = TMPTextPatch.NormalizeUsernamePlaceholders(stripped, out _);
            var step2 = TMPTextPatch.NormalizeNumbers(step1, out _);
            var key   = TMPTextPatch.NormalizeMaps(step2, out _);
            if (string.IsNullOrWhiteSpace(key) || key.Length < 2) return;
            set.Add(key);
        }

        private void RefreshVisibility(RowListPanel panel)
        {
            var searchText = panel.FilterInput.text.Trim();
            foreach (var row in panel.Rows)
            {
                bool pass = panel.Filter.Matches(
                    row.Original, row.TransFld.text, row.RawOriginal,
                    row.IsTranslated, row.IsIgnored, row.Category, searchText);
                row.Root.SetActive(pass);
            }
        }

        private void UpdateSaveAllBtn(RowListPanel panel)
        {
            if (panel.SaveAllText == null) return;
            int n = panel.Rows.Count(r => r.IsModified);
            panel.SaveAllText.text = n > 0 ? $"Salvar {n} modificada(s)" : "Salvar todas modificadas";
        }

    }
}
