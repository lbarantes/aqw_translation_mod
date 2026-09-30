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
using AQWMod.Localization; // Plugin.Log — ver LogSpriteResolutionOnce()

namespace AQWMod.Localization.Overlay
{
    // Helpers genéricos de construção de UI (Go/Img/HRow/Lbl/Btn/Fld/GetFont/Shorten).
    public partial class TranslationEditorOverlay
    {
        private static GameObject Go(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            return go;
        }

        // mapeia cada cor de fundo "de papel" já usada no overlay pra um
        // sprite real da UI do AQW (GameUiAssets.cs), sem tocar em nenhum
        // call site de Img/HRow/Btn — todos já passam por Img() abaixo. Se o
        // sprite não estiver carregado em memória nesta sessão, cai de volta
        // pro retângulo de cor lisa, nunca quebra.
        //
        // lazy (Dictionary construído na primeira chamada, não como
        // inicializador estático): os campos Bg/BgHdr/etc. são de outro
        // arquivo desta mesma partial class, e a ordem de inicialização de
        // campos estáticos entre arquivos de uma partial class não é
        // garantida — referenciá-los num inicializador estático aqui
        // arriscaria pegá-los ainda como Color.clear. Adiar pro método evita
        // isso: todo campo estático já foi inicializado antes de qualquer
        // método de instância rodar.
        private static Dictionary<Color, string>? s_bgSpriteRoles;
        private static Dictionary<Color, string> BgSpriteRoles => s_bgSpriteRoles ??= new Dictionary<Color, string>
        {
            // corpo externo de janela/modal — só cantos quadrados; a
            // distinção visual entre corpo/cabeçalho vem do tint (mais escuro no corpo)
            { Bg,       "9 Slice Square Window BG grey" },
            { BgHdr,    "9 Slice Square Window BG grey" },
            { BgBtn,    "Primary Button Fill" },       // botão primário, tint verde por cima
            { BgClose,  "9 Sliced Red Button" },       // sprite já é um botão vermelho
            // chip e botão secundário/terciário: mesma forma genérica,
            // diferenciados pelo tint (BgChip/BgChipOn/BgIgnBtn)
            { BgChip,   "Primary Button Fill" },
            { BgChipOn, "Primary Button Fill" },
            { BgIgnBtn, "Primary Button Fill" },
        };

        // moldura dourada característica dos botões do AQW, camada separada
        // por cima do preenchimento colorido (Btn() abaixo), sem tint — a
        // cor de identidade já vem do preenchimento embaixo. "9 Sliced Red
        // Button" (BgClose) já é autocontido com sua própria borda, não
        // recebe essa camada extra.
        private static Dictionary<Color, string>? s_frameOverlayRoles;
        private static Dictionary<Color, string> FrameOverlayRoles => s_frameOverlayRoles ??= new Dictionary<Color, string>
        {
            { BgBtn,    "Primary Button Frame" },
            { BgChip,   "Primary Button Frame" },
            { BgChipOn, "Primary Button Frame" },
            { BgIgnBtn, "Primary Button Frame" },
        };

        private static void Img(GameObject go, Color col)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();

            Sprite? sprite = BgSpriteRoles.TryGetValue(col, out var spriteName)
                ? GameUiAssets.TryGetSprite(spriteName)
                : null;

            if (sprite != null)
            {
                img.sprite = sprite;
                img.type   = Image.Type.Sliced;
            }
            else
            {
                img.sprite = null;
                img.type   = Image.Type.Simple;
            }

            // tint preservado nos dois casos — mesma linguagem de cor de
            // sempre (verde=confirmar, vermelho=fechar/apagar), só que agora
            // por cima de uma textura de verdade quando disponível
            img.color = col;
        }

        // diagnóstico uma vez por sessão, chamado no fim de BuildUI() — não é
        // necessário pra usar a GUI. Usa Plugin.Log (não Debug.Log — o
        // listener do BepInEx pro console nativo do Unity não espelha
        // Debug.Log/Info pro LogOutput.log neste setup, só Plugin.Log é
        // garantido ali). Além de checar os nomes de BgSpriteRoles, despeja
        // uma amostra dos nomes reais de Sprite já carregados que parecem
        // ser de UI — os nomes em BgSpriteRoles vieram de uma varredura de
        // string no binário, sem garantia de bater com o Sprite.name real.
        private static bool s_spriteResolutionLogged;
        private static void LogSpriteResolutionOnce()
        {
            if (s_spriteResolutionLogged) return;
            s_spriteResolutionLogged = true;

            var names = new HashSet<string>(BgSpriteRoles.Values, StringComparer.Ordinal);
            names.UnionWith(FrameOverlayRoles.Values);
            int ok = 0;
            var details = new List<string>();
            foreach (var name in names)
            {
                bool found = GameUiAssets.TryGetSprite(name) != null;
                if (found) ok++;
                details.Add($"'{name}': {(found ? "OK" : "não encontrado")}");
            }
            Plugin.Log.LogInfo(
                $"[AQWTranslation][Reskin] {ok}/{names.Count} sprites do mapa resolvidos — " +
                string.Join("; ", details));

            string[] keywords = { "panel", "button", "window", "border", "slice", "bg", "accept", "close", "frame" };
            var uiSpriteNames = new List<string>();
            var allNames      = new List<string>();
            int totalSprites  = 0;
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null) continue;
                totalSprites++;
                if (string.IsNullOrEmpty(sprite.name)) continue;
                allNames.Add(sprite.name);
                var lower = sprite.name.ToLowerInvariant();
                if (Array.Exists(keywords, kw => lower.Contains(kw)))
                    uiSpriteNames.Add(sprite.name);
            }

            var sample = uiSpriteNames.Distinct(StringComparer.Ordinal)
                                      .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                      .Take(200)
                                      .ToList();
            Plugin.Log.LogInfo(
                $"[AQWTranslation][Reskin] {sample.Count} sprite(s) carregados com nome parecido de UI: " +
                string.Join(" | ", sample));

            // se o filtro acima veio vazio, isto diz se é porque não há
            // Sprite nenhum carregado ou porque há sprite carregado mas com
            // nome que não bate com palavra-chave nenhuma
            var unfiltered = allNames.Distinct(StringComparer.Ordinal)
                                      .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                      .Take(80)
                                      .ToList();
            Plugin.Log.LogInfo(
                $"[AQWTranslation][Reskin] Total de Sprites em memória agora: {totalSprites} " +
                $"({allNames.Count} com nome não-vazio). Amostra sem filtro: " +
                string.Join(" | ", unfiltered));
        }

        // diagnóstico de tamanho real do RectTransform — roda uma vez, na
        // primeira vez que a janela principal fica visível (RectTransform só
        // recalcula o layout com o GameObject ativo, por isso não dá pra
        // medir dentro de BuildUI(), que roda com a janela ainda desativada)
        private bool _sizeDiagLogged;
        private void LogButtonSizeDiagnosticOnce()
        {
            if (_sizeDiagLogged) return;
            _sizeDiagLogged = true;
            StartCoroutine(LogButtonSizeDiagnosticCoroutine());
        }

        private System.Collections.IEnumerator LogButtonSizeDiagnosticCoroutine()
        {
            // duas esperas: o Canvas só recalcula layout no fim do frame, uma
            // folga extra evita pegar um estado ainda no meio do rebuild
            yield return null;
            yield return null;

            if (_window == null) yield break;
            var hdrRow = _window.transform.childCount > 0 ? _window.transform.GetChild(0) as RectTransform : null;
            Plugin.Log.LogInfo(
                $"[AQWTranslation][Reskin][SizeDiag] Window rect: {((RectTransform)_window.transform).rect}");
            Plugin.Log.LogInfo(
                $"[AQWTranslation][Reskin][SizeDiag] Header row '{hdrRow?.name}' rect: {hdrRow?.rect}, " +
                $"sizeDelta: {hdrRow?.sizeDelta}, childCount: {hdrRow?.childCount}");

            if (hdrRow == null) yield break;
            for (int i = 0; i < hdrRow.childCount; i++)
            {
                var child = hdrRow.GetChild(i) as RectTransform;
                if (child == null) continue;
                Plugin.Log.LogInfo(
                    $"[AQWTranslation][Reskin][SizeDiag]  header child[{i}] '{child.name}' rect: {child.rect}, " +
                    $"anchoredPos: {child.anchoredPosition}, grandchildren: {child.childCount}");

                // se este filho for um botão (Image/Button com netos tipo
                // Fill/Frame/T), desce mais um nível pra ver se algum já vem
                // com tamanho estranho
                for (int j = 0; j < child.childCount; j++)
                {
                    var grandchild = child.GetChild(j) as RectTransform;
                    if (grandchild == null) continue;
                    Plugin.Log.LogInfo(
                        $"[AQWTranslation][Reskin][SizeDiag]    -> '{grandchild.name}' rect: {grandchild.rect}, " +
                        $"anchorMin: {grandchild.anchorMin}, anchorMax: {grandchild.anchorMax}, " +
                        $"offsetMin: {grandchild.offsetMin}, offsetMax: {grandchild.offsetMax}");
                }
            }
        }

        // mesmo truque de sizeDelta explícito do HRow() abaixo, reaproveitado
        // por qualquer elemento filho direto de um VerticalLayoutGroup com
        // childControlHeight=false (ex.: os botões do dropdown "+" em
        // CreateNode.cs) — sem isto a altura ficaria só na mão do
        // LayoutElement, que HRow() já mostrou não ser confiável sozinho aqui
        private static void BakeExplicitHeight(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, height);
        }

        private static GameObject HRow(GameObject parent, int height, Color bg)
        {
            var go = Go("HRow", parent.transform);
            Img(go, bg);

            float h = Mathf.Max(height, MinButtonHeight);

            // altura fixada direto no RectTransform da linha, não deixada
            // pro VerticalLayoutGroup do pai "computar". Causa raiz do bug de
            // cabeçalho gigante: elementos montados em BuildUI() com a janela
            // ainda desativada (_window.SetActive(false)) ficam fora da fila
            // de rebuild de layout do Unity (adiado pro fim do frame, e um
            // objeto inativo nunca entra nessa fila) — a altura ficava travada
            // no que o Text "cru" reportava, bem maior que o pedido. As
            // linhas da árvore nunca tiveram esse problema porque são criadas
            // com a janela já visível. Setar sizeDelta aqui elimina a corrida
            // de vez: pivot/anchor abaixo fazem a altura vir só de
            // sizeDelta.y, sem depender de nenhum rebuild rodar antes.
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, h);

            // ainda usado pela lista de árvore (_listContent tem seu próprio
            // VerticalLayoutGroup com childControlHeight=true, que lê isto
            // pra dimensionar/scrollar as linhas dinamicamente)
            var rowLe = go.AddComponent<LayoutElement>();
            rowLe.layoutPriority  = 100;
            rowLe.preferredHeight = h;
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(6, 6, 2, 2); hl.spacing = 4;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            return go;
        }

        private static Text Lbl(GameObject parent, string text, int size,
                                  FontStyle fs, Color col, float fixedW, bool flex)
        {
            var go = Go("Lbl", parent.transform);
            var le = go.AddComponent<LayoutElement>();
            if (!flex) { le.preferredWidth = fixedW; le.flexibleWidth = 0; }
            else          le.flexibleWidth = 1;
            // sem preferredHeight, o Text (que também implementa
            // ILayoutElement) reporta uma altura enorme (~97px, mesmo pra
            // texto vazio). Só setar preferredHeight pequeno não basta:
            // quando dois ILayoutElement no mesmo GameObject têm a mesma
            // prioridade (LayoutElement e Text usam 0 por padrão), o Unity
            // pega o MAIOR valor entre os dois, não o "explícito" — subir a
            // prioridade do LayoutElement faz ele vencer de verdade.
            le.layoutPriority  = 100;
            le.preferredHeight = size * 1.6f;
            var t = go.AddComponent<Text>();
            t.font = GetFont(); t.text = text; t.fontSize = size;
            t.fontStyle = fs; t.color = col;
            t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = false;
            // rótulo é sempre de uma linha só — sem chance do wrap
            // contribuir pra uma altura "preferida" maior que a esperada
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Truncate;
            return t;
        }

        // reduz a espessura aparente da borda 9-slice em botões pequenos —
        // os sprites reais do AQW parecem desenhados pra botões bem maiores
        // que os ~20-30px usados aqui, sem isso a borda fica desproporcional
        private const float ButtonSpritePpuMultiplier = 2.5f;

        // altura mínima de botão — quem pedir menos que isso é elevado até
        // aqui, quem já pedia mais alto não é afetado
        private const float MinButtonHeight = 24f;

        private static Button Btn(GameObject parent, string label,
                                   float w, float h, Color bg, Action onClick)
        {
            h = Mathf.Max(h, MinButtonHeight);

            var go = Go("Btn", parent.transform);
            var le = go.AddComponent<LayoutElement>();
            le.layoutPriority = 100; // ver nota de layoutPriority em HRow()
            // minWidth = w também, senão o HorizontalLayoutGroup pode
            // espremer o botão abaixo da largura pedida
            le.minWidth = w; le.preferredWidth = w; le.preferredHeight = h; le.flexibleWidth = 0;

            bool hasFrame = FrameOverlayRoles.TryGetValue(bg, out var frameName)
                            && GameUiAssets.TryGetSprite(frameName!) != null;

            // preenchimento recuado alguns pixels da borda quando há moldura
            // por cima: o sprite de preenchimento é reto (sem cantos
            // arredondados), a moldura é arredondada — sem o recuo, a quina
            // quadrada do preenchimento vaza pra fora do contorno da
            // moldura. Sem moldura, recuo = 0, preenche 100% como sempre.
            var fillGo = Go("Fill", go.transform);
            var firt   = fillGo.GetComponent<RectTransform>();
            float inset = hasFrame ? 3f : 0f;
            firt.anchorMin = Vector2.zero; firt.anchorMax = Vector2.one;
            firt.offsetMin = new Vector2(inset, inset);
            firt.offsetMax = new Vector2(-inset, -inset);
            Img(fillGo, bg);
            var fillImg = fillGo.GetComponent<Image>();
            // só afina a borda do preenchimento quando ele tem moldura por
            // cima — sprite autocontido como "9 Sliced Red Button" (BgClose)
            // já vem correto sem ajuste, aplicar o mesmo multiplicador nele
            // afinaria a borda até ela sumir
            if (hasFrame && fillImg.sprite != null)
                fillImg.pixelsPerUnitMultiplier = ButtonSpritePpuMultiplier;

            var btn = go.AddComponent<Button>();
            var cs  = btn.colors;
            cs.normalColor = Color.white; cs.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            cs.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            btn.colors = cs; btn.targetGraphic = fillImg;

            // moldura dourada por cima do preenchimento, puramente
            // decorativa, não captura clique
            if (hasFrame)
            {
                var frameSprite = GameUiAssets.TryGetSprite(frameName!);
                var frameGo = Go("Frame", go.transform);
                var frt = frameGo.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = frt.offsetMax = Vector2.zero;
                var frameImg = frameGo.AddComponent<Image>();
                frameImg.sprite = frameSprite;
                frameImg.type   = Image.Type.Sliced;
                frameImg.pixelsPerUnitMultiplier = ButtonSpritePpuMultiplier;
                frameImg.color  = Color.white;
                frameImg.raycastTarget = false;
            }

            var tgo = Go("T", go.transform);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.AddComponent<Text>();
            t.font = GetFont(); t.text = label; t.fontSize = 11;
            t.color = ColWhite; t.alignment = TextAnchor.MiddleCenter;
            // rótulo de botão é sempre de uma linha — senão um rótulo um
            // pouco mais largo que o botão quebrava em duas linhas
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Overflow;
            if (onClick != null) btn.onClick.AddListener(onClick.Invoke);
            return btn;
        }

        private static InputField Fld(GameObject parent, float w, float h,
                                       string placeholder, bool flex = false)
        {
            // separar o LayoutElement do InputField é essencial: no mesmo
            // GameObject, o Unity às vezes usa a largura preferida do texto
            // pra sobrescrever o LayoutElement. Aqui ele fica em "go" e o
            // InputField em "inner" (filho), garantindo que o
            // HorizontalLayoutGroup veja só o LayoutElement.
            var go = Go("Fld", parent.transform);
            var le = go.AddComponent<LayoutElement>();
            le.layoutPriority = 100; // ver nota de layoutPriority em HRow()
            if (!flex) { le.minWidth = w; le.preferredWidth = w; le.flexibleWidth = 0; }
            else          le.flexibleWidth = 1;
            le.preferredHeight = h;
            go.AddComponent<RectMask2D>(); // recorta visualmente texto que ultrapasse os limites

            var inner = Go("Inner", go.transform);
            var irt   = inner.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            Img(inner, BgInput);
            var fld = inner.AddComponent<InputField>();

            var pgo = Go("PH", inner.transform);
            var prt = pgo.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(4, 0); prt.offsetMax = new Vector2(-4, 0);
            var pt = pgo.AddComponent<Text>();
            pt.font = GetFont(); pt.text = placeholder; pt.fontSize = 10;
            pt.color = ColGray; pt.fontStyle = FontStyle.Italic;
            pt.alignment = TextAnchor.MiddleLeft;

            var tgo = Go("T", inner.transform);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4, 0); trt.offsetMax = new Vector2(-4, 0);
            var tt = tgo.AddComponent<Text>();
            tt.font = GetFont(); tt.fontSize = 11; tt.color = ColWhite;
            tt.alignment = TextAnchor.MiddleLeft; tt.supportRichText = false;

            fld.targetGraphic = inner.GetComponent<Image>();
            fld.placeholder   = pt;
            fld.textComponent = tt;
            return fld;
        }

        private RectTransform BuildScrollView(GameObject parent, float height)
        {
            var sv = Go("Scroll", parent.transform);
            Img(sv, Color.clear);
            var svRt = sv.GetComponent<RectTransform>();
            svRt.anchorMin = new Vector2(0, 1);
            svRt.anchorMax = new Vector2(1, 1);
            svRt.pivot     = new Vector2(0.5f, 1);
            svRt.sizeDelta = new Vector2(0, height); // ver nota de sizeDelta em HRow()
            sv.AddComponent<LayoutElement>().preferredHeight = height;
            var sr = sv.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 35;
            var vp = Go("VP", sv.transform);
            var vrt = vp.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            Img(vp, new Color(0, 0, 0, 0.01f));
            vp.AddComponent<Mask>().showMaskGraphic = false;
            var ct = Go("Ct", vp.transform);
            var crt = ct.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1); crt.offsetMin = crt.offsetMax = Vector2.zero;
            var vlg = ct.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2; vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            ct.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = crt; sr.viewport = vrt;
            return crt;
        }

        private static string Shorten(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";

    }
}
