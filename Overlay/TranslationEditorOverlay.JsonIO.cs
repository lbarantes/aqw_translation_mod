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
    // Leitura de uncategorized.json (via TranslationFileStore). Só leitura:
    // a GUI não grava mais tradução nele (ver NeedsDestination em Rows.cs).
    public partial class TranslationEditorOverlay
    {
        // ReadFile delega pro TranslationFileStore (JObject de
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
    }
}
