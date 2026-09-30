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
    // Aba Configurações — edita ModConfig diretamente.
    public partial class TranslationEditorOverlay
    {
        // edita ModConfig direto — BepInEx já salva o .cfg a cada Value
        // alterado, então não precisa de um botão "Salvar" separado aqui
        private void ToggleSettings()
        {
            if (_viewMode == ViewMode.Settings) { DoTree(); return; }
            DoSettings();
        }

        private void DoSettings()
        {
            foreach (var row in _rows)
                if (!string.IsNullOrWhiteSpace(row.TransFld.text))
                    _buffers[row.Original] = row.TransFld.text;

            _viewMode = ViewMode.Settings;
            if (_imgSettings != null) _imgSettings.color = BgChipOn;

            foreach (Transform t in _listContent) Destroy(t.gameObject);
            _rows.Clear();

            void Section(string title)
            {
                var h = HRow(_listContent.gameObject, 20, BgHdr);
                Lbl(h, title, 10, FontStyle.Bold, ColGray, 0, true);
            }

            void BoolRow(string label, string desc, BepInEx.Configuration.ConfigEntry<bool> entry)
            {
                var row = HRow(_listContent.gameObject, 26, BgRow);
                Lbl(row, label, 10, FontStyle.Normal, ColWhite, 190, false);
                Button btn = null!;
                btn = Btn(row, entry.Value ? "Ativado" : "Desativado", 92, 22,
                    entry.Value ? BgBtn : BgIgnBtn, () =>
                    {
                        entry.Value = !entry.Value;
                        var t = btn.GetComponentInChildren<Text>();
                        if (t != null) t.text = entry.Value ? "Ativado" : "Desativado";
                        var img = btn.GetComponentInChildren<Image>();
                        if (img != null) img.color = entry.Value ? BgBtn : BgIgnBtn;
                    });
                Lbl(row, desc, 9, FontStyle.Italic, ColGray, 0, true);
            }

            void IntRow(string label, string desc, BepInEx.Configuration.ConfigEntry<int> entry)
            {
                var row = HRow(_listContent.gameObject, 26, BgRow);
                Lbl(row, label, 10, FontStyle.Normal, ColWhite, 190, false);
                var fld = Fld(row, 70, 22, "", flex: false);
                fld.text = entry.Value.ToString();
                fld.onEndEdit.AddListener(v =>
                {
                    if (int.TryParse(v, out var n)) entry.Value = n;
                    fld.text = entry.Value.ToString();
                });
                Lbl(row, desc, 9, FontStyle.Italic, ColGray, 0, true);
            }

            Section("Geral");
            BoolRow("Mod ativado", "Desativa toda a tradução.", ModConfig.Enabled);
            IntRow("Cache (nº entradas)", "Capacidade do cache LRU de traduções.", ModConfig.CacheCapacity);

            Section("Captura de textos sem tradução");
            BoolRow("Captura automática", "Salva strings sem tradução em translations/_capture/.", ModConfig.CaptureEnabled);
            IntRow("Tamanho mínimo p/ capturar", "Strings mais curtas que isso não são capturadas.", ModConfig.CaptureMinLength);
            IntRow("Intervalo de flush (min)", "De quanto em quanto tempo _capture/ é salvo em disco.", ModConfig.CaptureFlushMins);
            BoolRow("Reforçar na varredura", "Reaplica tradução em textos visíveis a cada ciclo de varredura — cobre casos que os patches não alcançam.", ModConfig.EnforceOnScan);

            Section("Hot Reload");
            BoolRow("Recarregar automaticamente", "Recarrega arquivos de tradução ao salvar, sem reiniciar o jogo.", ModConfig.HotReloadEnabled);
            IntRow("Debounce (ms)", "Tempo mínimo entre recargas do mesmo arquivo.", ModConfig.HotReloadDebounce);

            Section("Debug / Diagnóstico");
            BoolRow("Log detalhado", "Loga cada tradução aplicada — impacta performance, use só para depurar.", ModConfig.VerboseLogging);
            BoolRow("Logar textos sem tradução", "Loga no console toda string sem tradução encontrada.", ModConfig.LogMisses);
            BoolRow("Estatísticas ao sair", "Mostra um resumo de cache/hits/misses no log ao fechar o jogo.", ModConfig.ShowStatsOnExit);

            _statsLabel.text = "Configurações — alterações são salvas imediatamente no .cfg";
        }

    }
}
