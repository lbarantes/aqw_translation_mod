using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AQWMod.Localization.Interceptors
{
    // Isola o overlay do jogo sem quebrar a própria UI dele.
    //
    // Teclado: bloqueia só quando um InputField do overlay está com foco
    // ativo — digitar na busca/tradução não chega ao jogo, mas WASD com o
    // overlay aberto (sem campo focado) funciona normal. F10/F11 sempre passam.
    //
    // Mouse: patchar Input.GetMouseButton* direto quebrava a própria UI do
    // overlay, porque o StandaloneInputModule (motor de UI da Unity) usa
    // esse mesmo método internamente. Solução: gate por ordem de execução —
    // EventSystem roda antes de tudo (execution order -1000), então um
    // Prefix em EventSystem.Update() zera o gate e um Postfix liga ele
    // depois. Durante o Update do EventSystem, GetMouseButton* passa livre;
    // depois disso (scripts do jogo), fica bloqueado se o cursor estiver
    // sobre a janela do overlay. Também patcha
    // EventSystem.IsPointerOverGameObject() pra avisar código bem-comportado
    // do jogo que há UI na frente.
    [HarmonyPatch]
    public static class InputBlockerPatch
    {
        // preenchidos por TranslationEditorOverlay.BuildUI()
        internal static bool           OverlayVisible = false;
        internal static RectTransform? OverlayWindow  = null;
        internal static Transform?     OverlayRoot    = null;

        // painéis extras (ex.: modal de detalhe) que são siblings fora do
        // RectTransform de OverlayWindow, mas cujo clique também precisa
        // ser bloqueado pro jogo — preenchido por quem constrói cada painel
        internal static readonly List<RectTransform> ExtraBlockedPanels = new();

        // false enquanto EventSystem processa, true depois (pros scripts do jogo)
        private static bool _gate = false;

        /// <summary>
        /// Bloqueia teclado só quando um InputField do overlay tem foco ativo.
        /// Botões, chips etc. não bloqueiam — WASD funciona normalmente.
        /// </summary>
        private static bool BlockKeys()
        {
            if (!OverlayVisible || OverlayRoot == null) return false;
            var sel = EventSystem.current?.currentSelectedGameObject;
            if (sel == null) return false;
            var fld = sel.GetComponent<InputField>();
            if (fld == null || !fld.isFocused) return false;
            // confirma que pertence ao overlay
            var t = sel.transform;
            while (t != null)
            {
                if (t == OverlayRoot) return true;
                t = t.parent;
            }
            return false;
        }

        private static bool MouseOverOverlay()
        {
            if (!OverlayVisible) return false;

            if (OverlayWindow != null &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    OverlayWindow, Input.mousePosition, null))
                return true;

            // painéis irmãos (ex.: modal de detalhe), fora do rect da janela
            // principal mas ainda parte do overlay
            foreach (var rt in ExtraBlockedPanels)
            {
                if (rt != null && rt.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(
                        rt, Input.mousePosition, null))
                    return true;
            }

            return false;
        }

        [HarmonyPatch(typeof(EventSystem), "Update")]
        [HarmonyPrefix]
        public static void EventSys_Pre()  => _gate = false; // vai processar

        [HarmonyPatch(typeof(EventSystem), "Update")]
        [HarmonyPostfix]
        public static void EventSys_Post() => _gate = true;  // terminou

        [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) })]
        [HarmonyPrefix]
        public static bool GetKey_Code(KeyCode key, ref bool __result)
        {
            if (!BlockKeys() || key == KeyCode.F10 || key == KeyCode.F11) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(string) })]
        [HarmonyPrefix]
        public static bool GetKey_Str(ref bool __result)
        {
            if (!BlockKeys()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
        [HarmonyPrefix]
        public static bool GetKeyDown_Code(KeyCode key, ref bool __result)
        {
            if (!BlockKeys() || key == KeyCode.F10 || key == KeyCode.F11) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(string) })]
        [HarmonyPrefix]
        public static bool GetKeyDown_Str(ref bool __result)
        {
            if (!BlockKeys()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) })]
        [HarmonyPrefix]
        public static bool GetKeyUp_Code(KeyCode key, ref bool __result)
        {
            if (!BlockKeys() || key == KeyCode.F10 || key == KeyCode.F11) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(string) })]
        [HarmonyPrefix]
        public static bool GetKeyUp_Str(ref bool __result)
        {
            if (!BlockKeys()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetAxis))]
        [HarmonyPrefix]
        public static bool GetAxis(ref float __result)
        {
            if (!BlockKeys()) return true;
            __result = 0f; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw))]
        [HarmonyPrefix]
        public static bool GetAxisRaw(ref float __result)
        {
            if (!BlockKeys()) return true;
            __result = 0f; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetButton))]
        [HarmonyPrefix]
        public static bool GetButton(ref bool __result)
        {
            if (!BlockKeys()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetButtonDown))]
        [HarmonyPrefix]
        public static bool GetButtonDown(ref bool __result)
        {
            if (!BlockKeys()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetButtonUp))]
        [HarmonyPrefix]
        public static bool GetButtonUp(ref bool __result)
        {
            if (!BlockKeys()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton))]
        [HarmonyPrefix]
        public static bool GetMouseButton(ref bool __result)
        {
            if (!_gate || !MouseOverOverlay()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown))]
        [HarmonyPrefix]
        public static bool GetMouseButtonDown(ref bool __result)
        {
            if (!_gate || !MouseOverOverlay()) return true;
            __result = false; return false;
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp))]
        [HarmonyPrefix]
        public static bool GetMouseButtonUp(ref bool __result)
        {
            if (!_gate || !MouseOverOverlay()) return true;
            __result = false; return false;
        }

        // StandaloneInputModule usa a sobrecarga com int (pointerId), não esta
        [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.IsPointerOverGameObject),
            new System.Type[0])]
        [HarmonyPrefix]
        public static bool Patch_IsPointerOverGameObject(ref bool __result)
        {
            if (!MouseOverOverlay()) return true;
            __result = true; return false;
        }
    }
}
