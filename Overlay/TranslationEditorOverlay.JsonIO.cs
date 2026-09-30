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
    // Leitura/escrita de uncategorized.json (via TranslationFileStore).
    public partial class TranslationEditorOverlay
    {
        // ReadFile/WriteFile delegam pro TranslationFileStore (JObject de
        // verdade), que entende tanto forma simples quanto estendida
        // ("ignore"/"context"). Um leitor de tokens manual antigo corrompia
        // silenciosamente entradas em forma de objeto, lendo campos internos
        // como se fossem chaves de tradução — corrigido delegando pra cá.
        private Dictionary<string, string> ReadFile()
        {
            try
            {
                return TranslationFileStore.Read(OutputFile)
                    .ToDictionary(e => e.Key, e => e.Text, StringComparer.Ordinal);
            }
            catch { return new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        private void WriteFile(Dictionary<string, string> dict)
        {
            TranslationFileStore.WriteAll(OutputFile, dict);

            // força recarga das traduções e descarte do cache por componente,
            // pra texto já visível na tela ser re-traduzido na hora
            TranslationManager.Instance.ReloadFile(OutputFile);
            TMPTextPatch.InvalidateSafeTranslateCache();
        }
    }
}
