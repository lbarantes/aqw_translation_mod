using HarmonyLib;
using UnityEngine.UI;

namespace AQWMod.Localization.Interceptors
{
    // Corrige o caret/seleção logo depois de inserir um token no editor
    // avançado (TranslationEditorOverlay.InsertToken).
    //
    // Isto é um patch Harmony, não um polling em Update/LateUpdate, porque
    // polling não resolvia: corrotina com atraso não funcionava, e mesmo
    // reafirmando em LateUpdate() por vários frames o InputField do próprio
    // Unity tem seu próprio LateUpdate() que roda na mesma fase — a ordem
    // relativa entre LateUpdate()s de componentes diferentes não é garantida,
    // então às vezes o dele rodava depois do nosso e sobrescrevia a correção.
    // Interceptando com Harmony os métodos do próprio InputField que mexem
    // em foco/seleção (OnFocus, ActivateInputFieldInternal, LateUpdate) com
    // um Postfix, nosso código roda na mesma call stack, sempre depois da
    // lógica do Unity que causava o problema — sem depender de ordenação.
    [HarmonyPatch]
    internal static class InputFieldCaretFix
    {
        private static InputField? _pendingField;
        private static int         _pendingPosition;
        private static int         _pendingFramesLeft;

        /// <summary>
        /// Chamado pelo overlay (InsertToken) pra agendar a correção: sempre
        /// que o Unity mexer em foco/seleção deste campo nos próximos frames,
        /// reaplica esta posição de caret sem seleção.
        /// </summary>
        internal static void RequestCaret(InputField field, int position, int frames = 15)
        {
            _pendingField      = field;
            _pendingPosition   = position;
            _pendingFramesLeft = frames;
            Reapply(field); // aplica já, caso o campo já esteja focado
        }

        private static void Reapply(InputField instance)
        {
            if (_pendingField == null || !ReferenceEquals(instance, _pendingField)) return;
            if (_pendingFramesLeft <= 0) { _pendingField = null; return; }

            instance.caretPosition           = _pendingPosition;
            instance.selectionAnchorPosition = _pendingPosition;
            instance.selectionFocusPosition  = _pendingPosition;
        }

        [HarmonyPatch(typeof(InputField), "OnFocus")]
        [HarmonyPostfix]
        public static void OnFocus_Post(InputField __instance) => Reapply(__instance);

        [HarmonyPatch(typeof(InputField), "ActivateInputFieldInternal")]
        [HarmonyPostfix]
        public static void ActivateInternal_Post(InputField __instance) => Reapply(__instance);

        [HarmonyPatch(typeof(InputField), "LateUpdate")]
        [HarmonyPostfix]
        public static void LateUpdate_Post(InputField __instance)
        {
            Reapply(__instance);
            // só decrementa quando é o LateUpdate do próprio campo alvo — a
            // contagem reflete frames reais dele, não de outro componente
            if (_pendingField != null && ReferenceEquals(__instance, _pendingField))
                _pendingFramesLeft--;
        }
    }
}
