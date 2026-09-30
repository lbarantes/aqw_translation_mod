using System;
using HarmonyLib;
using UnityEngine.UI;

namespace AQWMod.Localization.Interceptors
{
    // Patch Harmony pra interceptar escrita em UnityEngine.UI.Text (o "Text"
    // legado, não-TMP). Antes disso, UI.Text só era alcançado pela varredura
    // periódica do overlay + reescrita manual, o que atrasava a tradução em
    // até ~1s depois do texto aparecer. UI.Text.text é um setter não-abstrato
    // (diferente de TMP_Text.text), então o mesmo Prefix usado em
    // TMPTextPatch.Prefix_TextSetter funciona aqui direto.
    //
    // A varredura periódica e o enforcement-on-scan continuam ativos de
    // propósito — o usuário prefere a lista do overlay se atualizando
    // sozinha. Este patch cobre a correção no momento da escrita (sem o
    // atraso); a varredura cobre a descoberta de texto novo pra GUI e serve
    // de rede de segurança pra texto que chega sem passar pelo setter.
    // Diferente do TMPTextPatch, não há patch de OnEnable aqui: UI.Text herda
    // de MaskableGraphic e a varredura já cobre esse caso, não vale o risco.
    [HarmonyPatch]
    public static class LegacyTextPatch
    {
        [HarmonyPatch(typeof(Text), nameof(Text.text), MethodType.Setter)]
        [HarmonyPrefix]
        public static bool Prefix_TextSetter(Text __instance, ref string value)
        {
            value = SafeTranslate(value, __instance);
            return true;
        }

        private static string SafeTranslate(string original, Text component)
        {
            if (string.IsNullOrWhiteSpace(original) || original.Length < 2) return original;
            if (component == null) return original;

            // ignora a própria overlay (mesma convenção do resto do mod)
            if (component.transform.root.name?.StartsWith("_AQW") == true) return original;

            // texto de InputField legado (chat, login) é conteúdo do usuário,
            // nunca traduz — mesma regra da varredura
            if (component.GetComponentInParent<InputField>() != null) return original;

            var translated = TMPTextPatch.TranslateRaw(original, component.transform);
            if (translated == original) return original;

            // atualiza o tracker legado — é dele que a varredura depende pra
            // saber que este componente já foi traduzido, senão o
            // enforcement-on-scan reescreveria por cima a cada tick
            var e = TMPTextPatch.LegacyOriginalTracker.GetOrCreateValue(component);
            e.Value      = original;
            e.Category   = TMPTextPatch.GetCategory(component.transform);
            e.Translated = translated;

            return translated;
        }
    }
}
