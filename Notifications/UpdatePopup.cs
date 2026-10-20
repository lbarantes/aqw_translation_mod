using System.Collections.Generic;
using UnityEngine;

namespace AQWMod.Localization.Notifications
{
    // Aviso de atualização no topo da tela ("o mod foi atualizado", "N
    // traduções novas"...). IMGUI de propósito: existe nas duas edições,
    // inclusive na Player, que não leva o código do editor (Overlay/).
    // Some ao clicar OK ou sozinho depois de AutoCloseSeconds — o cursor do
    // jogo pode estar preso durante o gameplay e o clique nunca acontecer.
    public sealed class UpdatePopup : MonoBehaviour
    {
        private const float AutoCloseSeconds = 30f;
        private const float Width = 560f;

        private static UpdatePopup? _instance;

        private readonly List<string> _lines = new List<string>();
        private float _shownAt;
        private GUIStyle? _bg, _title, _text, _button;

        /// <summary>Acrescenta uma linha ao aviso (abre se estiver fechado). Só no main thread.</summary>
        public static void Show(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (_instance == null)
            {
                // prefixo "_AQW": a varredura de textos ignora objetos do próprio mod
                var go = new GameObject("_AQWUpdatePopup");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<UpdatePopup>();
            }
            if (!_instance._lines.Contains(line!)) _instance._lines.Add(line!);
            _instance._shownAt = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (_lines.Count == 0) return;
            if (Time.unscaledTime - _shownAt > AutoCloseSeconds) { _lines.Clear(); return; }

            EnsureStyles();
            // mesma escala em qualquer resolução (referência 1080p)
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            const float pad = 16f, titleH = 26f, buttonH = 30f, gap = 8f;
            float inner = Width - pad * 2;
            float textH = 0f;
            var contents = new List<GUIContent>();
            foreach (var l in _lines)
            {
                var c = new GUIContent("• " + l);
                contents.Add(c);
                textH += _text!.CalcHeight(c, inner) + 4f;
            }
            float height = pad + titleH + gap + textH + gap + buttonH + pad;
            var rect = new Rect((Screen.width / scale - Width) / 2f, 40f, Width, height);

            GUI.Box(rect, GUIContent.none, _bg);
            float y = rect.y + pad;
            GUI.Label(new Rect(rect.x + pad, y, inner, titleH), "Tradução PT-BR atualizada", _title);
            y += titleH + gap;
            foreach (var c in contents)
            {
                float h = _text!.CalcHeight(c, inner);
                GUI.Label(new Rect(rect.x + pad, y, inner, h), c, _text);
                y += h + 4f;
            }
            if (GUI.Button(new Rect(rect.xMax - pad - 90f, rect.yMax - pad - buttonH, 90f, buttonH), "OK", _button))
                _lines.Clear();

            GUI.matrix = oldMatrix;
        }

        private void EnsureStyles()
        {
            if (_bg != null) return;
            _bg = new GUIStyle(GUI.skin.box) { normal = { background = Solid(new Color(0.06f, 0.06f, 0.10f, 0.95f)) } };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            _text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, normal = { textColor = Color.white } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
