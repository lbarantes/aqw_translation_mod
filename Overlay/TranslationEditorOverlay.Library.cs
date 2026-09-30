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
    // Helpers compartilhados: população de linhas (usada só pelo Scan) e
    // caminho/hierarquia de componente de texto.
    public partial class TranslationEditorOverlay
    {
        private void PopulateRows(RowListPanel panel, bool accumulate,
            List<(string key, string rawOrig, string saved, string cat, string path, string sourceFile)> entries)
        {
            entries.Sort((a, b) => EntryOrdering.Compare(a.key, a.saved, b.key, b.saved));

            for (int i = 0; i < entries.Count; i++)
            {
                var (key, rawOrig, saved, cat, path, sourceFile) = entries[i];
                var buf = panel.Buffers.TryGetValue(key, out var b) ? b : saved;
                panel.Rows.Add(BuildRow(panel, key, rawOrig, saved, cat, path, buf, i % 2 == 1, sourceFile));
            }

            int miss = entries.Count(e => string.IsNullOrEmpty(e.saved));
            int ign  = entries.Count(e => !string.IsNullOrEmpty(e.saved) && e.saved == e.key);
            panel.StatsLabel.text = accumulate
                ? $"{entries.Count} hist.  {miss} sem trad.  {ign} ign."
                : $"{entries.Count} vis.  {miss} sem trad.  {ign} ign.";

            RefreshVisibility(panel);
            UpdateSaveAllBtn(panel);
        }

        // caminho completo na hierarquia + tipo do componente; genérico por
        // Transform, serve TMP_Text e UnityEngine.UI.Text
        private static string PathOf(Transform t, string typeName)
        {
            var parts = new List<string>();
            var cur   = t;
            while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
            parts.Reverse();
            return string.Join(" > ", parts) + $"  [{typeName}]";
        }
    }
}
