using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using AQWMod.Localization.Core;
using AQWMod.Localization.Capture;
using AQWMod.Localization.Utils;

namespace AQWMod.Localization.Interceptors
{
    // Patches Harmony pra interceptar escrita em TMP_Text.
    //
    // TMP_Text é abstrata, e o Harmony/MonoMod desta versão do BepInEx não
    // consegue fazer bind de "ref string __0" em método abstrato (falha com
    // "Parameter X not found"). Por isso a classe tem dois blocos: A) o
    // setter "text", que não é abstrato, usa patch normal por atributo; B)
    // SetText(string) e overloads são registrados manualmente via
    // AccessTools/HarmonyMethod, cada um em try/catch isolado pra falha de
    // um não quebrar os outros. O guard [ThreadStatic] evita loop infinito
    // entre os dois blocos.
    [HarmonyPatch]
    public static class TMPTextPatch
    {
        [ThreadStatic]
        private static bool _isTranslating;

        // cache por componente pra não reprocessar texto idêntico.
        // ConditionalWeakTable usa weak-keys, GC coleta normal. Quando o
        // overlay salva uma tradução nova, o cache é descartado pra forçar
        // re-tradução dos textos que agora têm entrada no JSON.
        private sealed class ComponentCache
        {
            public string LastRaw    = "";
            public string LastResult = "";
        }
        private static ConditionalWeakTable<TMP_Text, ComponentCache> _componentCache
            = new ConditionalWeakTable<TMP_Text, ComponentCache>();

        /// <summary>
        /// Descarta o cache de todos os componentes.
        /// Chamado pelo overlay após salvar traduções novas/editadas.
        /// </summary>
        internal static void InvalidateSafeTranslateCache()
            => _componentCache = new ConditionalWeakTable<TMP_Text, ComponentCache>();

        // mapeia cada TMP_Text -> texto original em inglês antes de traduzir,
        // pro overlay mostrar o original correto mesmo com a UI já exibindo PT-BR
        internal sealed class OriginalEntry
        {
            public string Value      = "";
            public string Category   = ""; // transform.root.name — usado para filtros por categoria
            public string Translated = ""; // último resultado traduzido escrito (só usado no caminho UI.Text legado)
        }
        internal static readonly ConditionalWeakTable<TMP_Text, OriginalEntry>
            OriginalTracker = new ConditionalWeakTable<TMP_Text, OriginalEntry>();

        /// <summary>
        /// Original rastreado deste componente, mas só se ele ainda vale.
        /// Balão de fala/texto de quest reaproveita o MESMO componente pra
        /// falas diferentes ("em andamento" -> "concluída, antes de entregar").
        /// Se o texto exibido não é mais exatamente o que a gente escreveu
        /// (Translated), o jogo trocou o texto e o rastreado é de outra fala —
        /// usá-lo mostraria a tradução antiga e esconderia o inglês novo do
        /// Scan (bug real, corrigido em 2026-09-24). Translated vazio (entrada
        /// antiga) confia como antes.
        /// </summary>
        internal static bool TryGetValidOriginal(TMP_Text tmp, out OriginalEntry entry)
        {
            if (OriginalTracker.TryGetValue(tmp, out entry)
                && !string.IsNullOrWhiteSpace(entry.Value)
                && (entry.Translated.Length == 0 || tmp.text == entry.Translated))
                return true;
            entry = null!;
            return false;
        }

        // captura pro overlay: DoTranslate só faz PUSH via CaptureRegistry.Record(),
        // o overlay faz PULL via Snapshot/RecentlySeen

        // preenchido quando um TMP_Text do container "Names" é detectado —
        // identifica nome de jogador sem '_' (ex.: "xow") em mensagens como
        // "Awesome landing, xow!"
        internal static readonly HashSet<string> KnownPlayerNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // carregado de npc_names.json: { "Booker": "Bibliotecário", "Maya": "Maya" }
        // chave vazia ou igual ao original = mantém sem tradução
        internal static readonly Dictionary<string, string> KnownNpcNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static void LoadNpcNames(string translationsRootPath)
        {
            KnownNpcNames.Clear();
            var file = System.IO.Path.Combine(translationsRootPath, "npc_names.json");
            if (!System.IO.File.Exists(file)) return;
            try
            {
                using var reader = new Newtonsoft.Json.JsonTextReader(
                    new System.IO.StreamReader(file, System.Text.Encoding.UTF8));
                string? k = null;
                while (reader.Read())
                {
                    if (reader.TokenType == Newtonsoft.Json.JsonToken.PropertyName)
                        k = reader.Value?.ToString();
                    else if (reader.TokenType == Newtonsoft.Json.JsonToken.String && k != null)
                    {
                        var val = reader.Value?.ToString() ?? "";
                        // valor vazio = identidade, NPC mantém o nome original em runtime
                        KnownNpcNames[k] = string.IsNullOrEmpty(val) ? k : val;
                        k = null;
                    }
                }
                Plugin.Log.LogInfo($"[AQWTranslation] {KnownNpcNames.Count} NPCs carregados de npc_names.json.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AQWTranslation] Falha ao carregar npc_names.json: {ex.Message}");
            }
        }

        // A) setter .text — funciona em método não-abstrato

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.text), MethodType.Setter)]
        [HarmonyPrefix]
        public static bool Prefix_TextSetter(TMP_Text __instance, ref string value)
        {
            value = SafeTranslate(value, __instance);
            return true;
        }

        // B) SetText via Postfix — a tradução é aplicada DEPOIS do método setar
        // o texto: SetText seta m_text/chama o setter, nosso Postfix lê o texto
        // já setado via __instance.text, e se há tradução aplica de volta
        // (também via __instance.text). O guard _isTranslating evita loop, já
        // que esse segundo set reaciona o Prefix_TextSetter. Isso cria um
        // atraso teórico de 1 frame, imperceptível na prática.

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText),
            new Type[] { typeof(string) })]
        [HarmonyPostfix]
        public static void Postfix_SetText_String(TMP_Text __instance)
        {
            ApplyTranslationPostfix(__instance);
        }

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText),
            new Type[] { typeof(string), typeof(bool) })]
        [HarmonyPostfix]
        public static void Postfix_SetText_StringBool(TMP_Text __instance)
        {
            ApplyTranslationPostfix(__instance);
        }

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText),
            new Type[] { typeof(string), typeof(float), typeof(float), typeof(float) })]
        [HarmonyPostfix]
        public static void Postfix_SetText_StringFloats(TMP_Text __instance)
        {
            ApplyTranslationPostfix(__instance);
        }

        // C) OnEnable postfix — pra texto serializado pelo Unity Editor: Unity
        // escreve m_text direto via serialização, sem chamar o setter.
        // OnEnable() é o primeiro ponto depois dos campos serializados
        // estarem populados, cobre texto de cena/prefab. Usa "object
        // __instance" (não TMP_Text) porque o Harmony pode aplicar este patch
        // em classes base como MaskableGraphic — o "is" garante que só
        // processa TMP_Text de fato.
        public static void Postfix_OnEnable(object __instance)
        {
            if (__instance is TMP_Text tmp)
                ApplyTranslationPostfix(tmp);
        }

        private static void ApplyTranslationPostfix(TMP_Text instance)
        {
            if (_isTranslating) return;
            if (instance == null) return;

            var current = instance.text;
            if (string.IsNullOrWhiteSpace(current) || current.Length < 2) return;

            var translated = SafeTranslate(current, instance);
            if (ReferenceEquals(translated, current)) return;
            if (translated == current) return;

            // aplica de volta — Prefix_TextSetter vai pegar, mas o guard impede loop
            _isTranslating = true;
            try { instance.text = translated; }
            finally { _isTranslating = false; }
        }

        /// <summary>
        /// Ponto de entrada público. Aplica cache por componente para evitar
        /// re-processar texto idêntico (reduz alocações GC no hotpath).
        /// </summary>
        internal static string SafeTranslate(string original, TMP_Text component)
        {
            if (_isTranslating) return original;
            if (string.IsNullOrWhiteSpace(original) || original.Length < 2) return original;

            // atalho rápido se o texto não mudou desde a última chamada
            var cache = _componentCache.GetOrCreateValue(component);
            if (cache.LastRaw == original) return cache.LastResult;

            var result = DoTranslate(original, component);
            cache.LastRaw    = original;
            cache.LastResult = result;
            return result;
        }

        private static string DoTranslate(string original, TMP_Text component)
        {
            if (IsNumericOrDate(original)) return original;
            if (IsDynamicGameContent(original)) return original;

            // nameplate de jogador: parent chamado "Names" (container do AQW) —
            // aproveita pra registrar o nome real, assim reconhecemos ele em
            // mensagens mesmo sem '_' (ex.: "Awesome landing, xow!")
            if (component.transform.parent != null &&
                string.Equals(component.transform.parent.name, "Names", StringComparison.Ordinal))
            {
                var plainName = original.Contains('<') ? StripRichTextTags(original) : original;
                plainName = plainName.Trim();
                if (plainName.Length >= 2)
                    KnownPlayerNames.Add(plainName);
                return original;
            }

            // pula TMP_Text dentro de TMP_InputField editável (texto digitado,
            // senha etc.) — InputField com readOnly=true é traduzível, o jogo usa
            // isso pra caixa de diálogo com scroll de quest (texto estático)
            {
                var parentInputField = component.GetComponentInParent<TMP_InputField>();
                if (parentInputField != null && !parentInputField.readOnly) return original;
            }

            try
            {
                var keyBase = original.Contains('<') ? StripRichTextTags(original).Trim() : original;
                keyBase = StripDialogCursor(keyBase);   // remove '_' de cursor ("Hello!_" -> "Hello!")
                if (string.IsNullOrWhiteSpace(keyBase) || keyBase.Length < 2) return original;

                // reaproveita GetCategory como sugestão de "context" pro Translate
                // desambiguar. Sempre cai de volta pra chave pura se não houver
                // entrada context-qualificada (nenhuma existe hoje) — é 100%
                // aditivo, só passa a valer no dia que alguém criar uma entrada
                // "texto@@categoria"
                var inferredContext = GetCategory(component.transform);

                // template "Goto map X to continue the quest \"Y\"" — tratado
                // antes da cadeia genérica: {map} nunca traduz, {quest} traduz
                // se houver entrada. Se casar, resolve tudo e retorna.
                if (TryNormalizeQuestGoto(keyBase, out var qKey, out var qMap, out var qQuest))
                {
                    var qPath = ComponentPathCache.GetOrBuild(component);

                    // push da chave do template + o nome da quest sozinho, pro
                    // usuário poder traduzir a quest direto no overlay
                    CaptureRegistry.Record(qKey, original,
                        inferredContext, GetPath(component),
                        null, null, new[] { qMap },
                        Time.realtimeSinceStartup, "tmp");
                    if (qQuest.Length >= 2)
                        CaptureRegistry.Record(qQuest, qQuest,
                            inferredContext, GetPath(component),
                            null, null, null, Time.realtimeSinceStartup, "tmp");

                    var qTemplate = TranslationManager.Instance.Translate(
                        qKey, new TranslationContext(qKey, qPath, context: inferredContext));
                    if (qTemplate == qKey) return original; // template sem tradução, fica literal

                    // Translate devolve o próprio texto quando não há entrada pra {quest}
                    var qQuestOut = TranslationManager.Instance.Translate(
                        qQuest, new TranslationContext(qQuest, qPath, context: inferredContext));

                    var qResult = qTemplate.Replace("{map}", qMap)
                                           .Replace("{quest}", qQuestOut);
                    if (qResult != original)
                    {
                        var qe      = OriginalTracker.GetOrCreateValue(component);
                        qe.Value      = original;
                        qe.Category   = inferredContext;
                        qe.Translated = qResult;
                    }
                    return qResult;
                }

                // cadeia primária, sem NPC: {player} -> {n1}/{n2} -> {map}.
                // NPC é fallback-only (abaixo) pra não quebrar tradução literal
                // existente tipo "Save Maya!" -> "Salve Maya!"
                var step1     = NormalizeUsernamePlaceholders(keyBase, out var capturedPlayers);
                var step2     = NormalizeNumbers(step1,   out var capturedNums);
                var lookupKey = NormalizeMaps(step2,      out var capturedMaps);

                // push pro overlay (captura cutscene/texto transiente).
                // Append/update-only: registrar a mesma chave duas vezes só
                // atualiza LastSeen/SeenCount, nunca duplica
                CaptureRegistry.Record(
                    lookupKey, original,
                    inferredContext, GetPath(component),
                    capturedPlayers, capturedNums, capturedMaps,
                    Time.realtimeSinceStartup, "tmp");

                if (ModConfig.VerboseLogging?.Value == true)
                    Plugin.Log.LogInfo($"[Capture] tmp: \"{Truncate(lookupKey, 60)}\"");

                var componentPath = ComponentPathCache.GetOrBuild(component);
                var ctx    = new TranslationContext(lookupKey, componentPath, context: inferredContext);
                var result = TranslationManager.Instance.Translate(lookupKey, ctx);

                if (result != lookupKey)
                {
                    if (capturedNums.Length    > 0) result = RestoreNumbers(result, capturedNums);
                    if (capturedMaps.Length    > 0) result = RestoreMaps(result, capturedMaps);
                    if (capturedPlayers.Length > 0) result = RestorePlaceholders(result, capturedPlayers);

                    if (result != original)
                    {
                        var entry      = OriginalTracker.GetOrCreateValue(component);
                        entry.Value      = original;
                        entry.Category   = GetCategory(component.transform);
                        entry.Translated = result;
                    }
                    return result;
                }

                // fallback {npc}: só ativa quando o usuário cadastrou uma entrada
                // com {npc}, ex.: "Talk to {npc} at intro to turn in this quest."
                // Tradução literal tipo "Save Maya!" continua funcionando porque a
                // busca primária acima já teria encontrado antes de chegar aqui
                if (KnownNpcNames.Count > 0)
                {
                    var npcNorm = NormalizeNpcNames(step1, out var capturedNpcs);
                    if (capturedNpcs.Length > 0)
                    {
                        var npcStep2    = NormalizeNumbers(npcNorm,  out var capturedNums2);
                        var fallbackKey = NormalizeMaps(npcStep2,    out var capturedMaps2);
                        if (fallbackKey != lookupKey)
                        {
                            var ctx2    = new TranslationContext(fallbackKey, componentPath, context: inferredContext);
                            var result2 = TranslationManager.Instance.Translate(fallbackKey, ctx2);
                            if (result2 != fallbackKey)
                            {
                                if (capturedNums2.Length   > 0) result2 = RestoreNumbers(result2, capturedNums2);
                                if (capturedMaps2.Length   > 0) result2 = RestoreMaps(result2, capturedMaps2);
                                if (capturedNpcs.Length    > 0) result2 = RestoreNpcNames(result2, capturedNpcs);
                                if (capturedPlayers.Length > 0) result2 = RestorePlaceholders(result2, capturedPlayers);

                                if (result2 != original)
                                {
                                    var entry      = OriginalTracker.GetOrCreateValue(component);
                                    entry.Value      = original;
                                    entry.Category   = GetCategory(component.transform);
                                    entry.Translated = result2;
                                }
                                return result2;
                            }
                        }
                    }
                }

                // Sem tradução → retorna original intacto (com markup e username real)
                return original;
            }
            catch (Exception ex)
            {
                Debug.TranslationLogger.StaticError(
                    $"Exceção em DoTranslate '{Truncate(original, 50)}': {ex.Message}");
                return original;
            }
        }

        // Tradução baseada em string, sem componente TMP — usada pelo
        // enforcement-on-scan pra texto que não é TMP_Text (UnityEngine.UI.Text
        // legado: diálogos/popups como "Goto map ... quest ..." e falas de NPC,
        // que nenhum patch Harmony alcança). Espelha a lógica de DoTranslate
        // (template {map}/{quest} + cadeia {player}/{n}/{map} + fallback
        // {npc}) mas sem as travas de componente (Names/InputField/cache),
        // já que o chamador (overlay) cuida de captura e idempotência. Não
        // registra no CaptureRegistry — o RecordVisible do overlay já faz isso.
        internal static string TranslateRaw(string original, Transform context)
        {
            if (string.IsNullOrWhiteSpace(original) || original.Length < 2) return original;
            if (IsNumericOrDate(original))      return original;
            if (IsDynamicGameContent(original)) return original;

            // nameplate de jogador (parent "Names"): nunca traduz
            if (context != null && context.parent != null &&
                string.Equals(context.parent.name, "Names", StringComparison.Ordinal))
                return original;

            try
            {
                var keyBase = original.Contains('<') ? StripRichTextTags(original).Trim() : original;
                keyBase = StripDialogCursor(keyBase);
                if (string.IsNullOrWhiteSpace(keyBase) || keyBase.Length < 2) return original;

                var path = context != null ? GetPath(context) : string.Empty;
                // mesma inferência aditiva de contexto do DoTranslate (reaproveita
                // GetCategory, que já trata null e cai de volta pra chave pura)
                var inferredContext = GetCategory(context);

                // template "Goto map X to continue the quest \"Y\""
                if (TryNormalizeQuestGoto(keyBase, out var qKey, out var qMap, out var qQuest))
                {
                    var qTemplate = TranslationManager.Instance.Translate(
                        qKey, new TranslationContext(qKey, path, context: inferredContext));
                    if (qTemplate == qKey) return original; // template sem tradução, fica literal
                    var qQuestOut = TranslationManager.Instance.Translate(
                        qQuest, new TranslationContext(qQuest, path, context: inferredContext));
                    return qTemplate.Replace("{map}", qMap).Replace("{quest}", qQuestOut);
                }

                // cadeia primária: {player} -> {n1}/{n2} -> {map}
                var step1     = NormalizeUsernamePlaceholders(keyBase, out var players);
                var step2     = NormalizeNumbers(step1,   out var nums);
                var lookupKey = NormalizeMaps(step2,      out var maps);

                var result = TranslationManager.Instance.Translate(
                    lookupKey, new TranslationContext(lookupKey, path, context: inferredContext));
                if (result != lookupKey)
                {
                    if (nums.Length    > 0) result = RestoreNumbers(result, nums);
                    if (maps.Length    > 0) result = RestoreMaps(result, maps);
                    if (players.Length > 0) result = RestorePlaceholders(result, players);
                    return result;
                }

                // fallback {npc}
                if (KnownNpcNames.Count > 0)
                {
                    var npcNorm = NormalizeNpcNames(step1, out var npcs);
                    if (npcs.Length > 0)
                    {
                        var npcStep2 = NormalizeNumbers(npcNorm, out var nums2);
                        var fbKey    = NormalizeMaps(npcStep2,   out var maps2);
                        if (fbKey != lookupKey)
                        {
                            var r2 = TranslationManager.Instance.Translate(
                                fbKey, new TranslationContext(fbKey, path, context: inferredContext));
                            if (r2 != fbKey)
                            {
                                if (nums2.Length   > 0) r2 = RestoreNumbers(r2, nums2);
                                if (maps2.Length   > 0) r2 = RestoreMaps(r2, maps2);
                                if (npcs.Length    > 0) r2 = RestoreNpcNames(r2, npcs);
                                if (players.Length > 0) r2 = RestorePlaceholders(r2, players);
                                return r2;
                            }
                        }
                    }
                }

                return original;
            }
            catch (Exception ex)
            {
                Debug.TranslationLogger.StaticError(
                    $"Exceção em TranslateRaw '{Truncate(original, 50)}': {ex.Message}");
                return original;
            }
        }

        internal static string GetPath(Transform t)
        {
            if (t == null) return string.Empty;
            var parts = new List<string>();
            var cur   = t;
            while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
            parts.Reverse();
            return string.Join(" > ", parts);
        }

        // espelha OriginalTracker, mas pra UnityEngine.UI.Text legado — guarda o
        // texto EN original quando o enforcement sobrescreve um UI.Text com a
        // tradução, pra captura/overlay continuarem mostrando o original, não o PT
        internal static readonly ConditionalWeakTable<UnityEngine.UI.Text, OriginalEntry> LegacyOriginalTracker
            = new ConditionalWeakTable<UnityEngine.UI.Text, OriginalEntry>();

        // hotpath sem Regex. True para string que não precisa de tradução:
        // só dígitos/símbolos ("42", "100/200", "12:30", "+5%", "---") ou
        // versão/sufixo curto ("v2.0.1", "1.5k", "75%") — critério: 0 letras
        // com algum dígito, ou <=2 letras com pelo menos o dobro de dígitos
        internal static bool IsNumericOrDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length < 2) return true;

            int digits  = 0;
            int letters = 0;
            foreach (char c in s)
            {
                if (char.IsDigit(c))  digits++;
                else if (char.IsLetter(c)) letters++;
            }

            if (letters == 0 && digits == 0) return true; // só símbolo/espaço: "---", ">>>"
            if (letters == 0 && digits > 0) return true;  // só número: "42", "100/200", "12:30"
            if (letters <= 2 && digits > 0 && digits >= letters * 2) return true; // sufixo curto: "1.5k", "v2.0"

            return false;
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "...";

        // nomes genéricos demais pra servir de categoria útil
        private static readonly HashSet<string> s_genericNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "canvas", "root", "uiroot", "ui root", "ui",
                "panel", "content", "container", "holder", "wrapper",
                "group", "layout", "layoutgroup",
                "background", "image", "rawimage", "text", "button",
                "scroll view", "scrollview", "viewport", "scrollrect", "mask",
                "inner", "outer", "main", "hud",
                "gameassets",   // raiz conhecida do AQW
            };

        /// <summary>
        /// Retorna o nome do ancestral mais próximo que faça sentido como
        /// categoria: primeiro tenta o filho direto da raiz (cobre
        /// "GameAssets -> LoginScreen -> ..."); se for genérico, sobe do
        /// componente até achar o primeiro nome não-genérico.
        /// </summary>
        internal static string GetCategory(Transform? t)
        {
            if (t == null) return "?";
            var root = t.root;
            if (root == t) return root.name;

            var directChild = t;
            while (directChild.parent != null && directChild.parent != root)
                directChild = directChild.parent;

            var dcName = StripClone(directChild.name);
            if (!s_genericNames.Contains(dcName) && dcName.Length >= 4)
                return dcName;

            var cur = t.parent;
            while (cur != null && cur != root)
            {
                var name = StripClone(cur.name);
                if (!s_genericNames.Contains(name) && name.Length >= 4)
                    return name;
                cur = cur.parent;
            }

            return root.name; // fallback: nome da raiz
        }

        // strip de tags rich-text TMP (<color=#FF...>, <b>, <size=N> etc.),
        // necessário antes de qualquer pattern-match porque o jogo pode emitir
        // "<color=#FFD700>battleon-1001</color>" onde o conteúdo útil
        // ("battleon-1001") não seria detectado pelos filtros de padrão.
        // Delega pra RichTextUtils.StripAll (mesma implementação regex-based
        // do pipeline, RichTextStage) em vez de manter uma segunda cópia da
        // lógica de "o que é uma tag" que podia divergir silenciosamente.
        // A normalização de negócio ({player}/{n}/{map}/{npc} logo abaixo)
        // continua aqui, fora do pipeline — DoTranslate tem ramificações
        // (template de quest, fallback de NPC condicional) que não cabem no
        // modelo linear de stages sem inventar uma arquitetura nova.
        internal static string StripRichTextTags(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.Contains('<')) return s;
            return RichTextUtils.StripAll(s).Trim();
        }

        // true para string gerada em runtime que não precisa de tradução:
        // timestamp (padrão NN/NN/NNNN, ex. "...time is: 05/28/2026 09:08:21"),
        // "mapname-N" sozinho (ex. "battleon-1001"), ou username/nick com '_'
        // sem espaço (ex. "Linck_", "Hero_AQW"). "N players in mapname-N" NÃO
        // é filtrado aqui — {n1}/{n2}/{map} normalizam isso e permitem uma
        // tradução genérica: "{n1} players in {map}-{n2}" -> "{n1} jogadores em {map}-{n2}".
        internal static bool IsDynamicGameContent(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2) return false;

            if (s.Contains('<')) s = StripRichTextTags(s);
            if (string.IsNullOrEmpty(s) || s.Length < 2) return false;

            // mensagem com timestamp (ex.: mensagem do Moderator terminando em
            // "The current server time is: 05/28/2026 09:08:21") — qualquer
            // string com NN/NN/NNNN é claramente gerada em runtime
            if (ContainsDateStamp(s)) return true;

            // as próximas duas regras só valem pra token sem espaço
            if (s.Contains(' ')) return false;

            // identificador de zona "mapname-N": antes do hífen só letra
            // minúscula (zona) vs. maiúscula (item/habilidade)
            {
                int hyphen = s.LastIndexOf('-');
                if (hyphen > 0 && hyphen < s.Length - 1)
                {
                    bool afterDigits = true;
                    for (int i = hyphen + 1; i < s.Length; i++)
                        if (!char.IsDigit(s[i])) { afterDigits = false; break; }

                    if (afterDigits)
                    {
                        bool beforeLower = true;
                        for (int i = 0; i < hyphen; i++)
                            if (!char.IsLower(s[i])) { beforeLower = false; break; }
                        if (beforeLower) return true;
                    }
                }
            }

            // username/nick: sem espaço e contém '_'
            if (s.Contains('_')) return true;

            return false;
        }

        // substitui sequência de dígitos por {n1}, {n2}, {n3}... Ex.:
        // "0/3 Frogzards Defeated" -> "{n1}/{n2} Frogzards Defeated". Só age
        // quando a string tem letra E dígito — string puramente numérica já
        // é filtrada por IsNumericOrDate.
        internal static string NormalizeNumbers(string s, out string[] captured)
        {
            captured = Array.Empty<string>();
            if (string.IsNullOrEmpty(s)) return s;

            bool hasLetter = false, hasDigit = false;
            foreach (char c in s)
            {
                if (char.IsLetter(c)) hasLetter = true;
                else if (char.IsDigit(c)) hasDigit = true;
                if (hasLetter && hasDigit) break;
            }
            if (!hasLetter || !hasDigit) return s;

            var caps = new List<string>();
            var sb   = new StringBuilder(s.Length + 20);
            int i    = 0;
            while (i < s.Length)
            {
                if (char.IsDigit(s[i]))
                {
                    int start = i;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                    caps.Add(s.Substring(start, i - start));
                    sb.Append($"{{n{caps.Count}}}");
                }
                else { sb.Append(s[i++]); }
            }

            if (caps.Count == 0) return s;
            captured = caps.ToArray();
            return sb.ToString();
        }

        internal static string RestoreNumbers(string s, string[] captured)
        {
            if (captured == null || captured.Length == 0) return s;
            for (int i = 0; i < captured.Length; i++)
                s = s.Replace($"{{n{i + 1}}}", captured[i]);
            return s;
        }

        // caminho completo na hierarquia de um TMP_Text (usado na captura)
        internal static string GetPath(TMP_Text tmp)
        {
            var parts = new List<string>();
            var cur   = tmp.transform;
            while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
            parts.Reverse();
            return string.Join(" > ", parts) + $"  [{tmp.GetType().Name}]";
        }

        // true se a string contiver NN/NN/NNNN (ex.: 05/28/2026). Sem Regex
        // pra manter o hotpath rápido.
        private static bool ContainsDateStamp(string s)
        {
            if (s.Length < 10) return false; // NN/NN/NNNN = 10 caracteres mínimo
            for (int i = 0; i <= s.Length - 10; i++)
            {
                if (char.IsDigit(s[i])   && char.IsDigit(s[i+1]) && s[i+2] == '/'
                 && char.IsDigit(s[i+3]) && char.IsDigit(s[i+4]) && s[i+5] == '/'
                 && char.IsDigit(s[i+6]) && char.IsDigit(s[i+7])
                 && char.IsDigit(s[i+8]) && char.IsDigit(s[i+9]))
                    return true;
            }
            return false;
        }

        // AQW adiciona '_' no final do diálogo como indicador visual "pressione
        // pra continuar" (ex.: "Hello Frogzards!_"). Remove só quando '_' é
        // precedido de pontuação, assim username terminado em '_' (ex.: "Linck_")
        // não é afetado: "Frogzards!_" -> "Frogzards!", mas "Linck_" fica "Linck_".
        internal static string StripDialogCursor(string s)
        {
            while (s.Length >= 2 && s[s.Length - 1] == '_')
            {
                char prev = s[s.Length - 2];
                if (prev == '.' || prev == '!' || prev == '?' ||
                    prev == ',' || prev == ';' || prev == ':')
                    s = s.Substring(0, s.Length - 1);
                else
                    break;
            }
            return s.Length > 0 ? s.TrimEnd() : s;
        }

        // "Awesome landing, Linck_!" muda pra cada jogador, não pode ter entrada
        // fixa no JSON. Solução: token com '_' (padrão de username do AQW) vira
        // {player}, {player2}, etc. — a chave fica "Awesome landing, {player}!"
        // e o placeholder é restaurado com o valor real em runtime. Só age em
        // string COM espaço (token isolado com '_' já é filtrado por
        // IsDynamicGameContent). Retorna a string normalizada; sem username,
        // retorna a mesma string e captured vazio.
        internal static string NormalizeUsernamePlaceholders(string s, out string[] captured)
        {
            captured = Array.Empty<string>();
            if (string.IsNullOrEmpty(s) || !s.Contains('_') || !s.Contains(' '))
                return s;

            var caps = new List<string>();
            var sb   = new StringBuilder(s.Length);
            int i    = 0;

            while (i < s.Length)
            {
                if (s[i] == ' ') { sb.Append(' '); i++; continue; }

                int start = i;
                while (i < s.Length && s[i] != ' ') i++;
                var token = s.Substring(start, i - start);

                if (!token.Contains('_')) { sb.Append(token); continue; }

                // isola o núcleo do token, sem pontuação envolvente
                int lo = 0, hi = token.Length;
                while (lo < hi && IsTokenPunct(token[lo]))  lo++;
                while (hi > lo && IsTokenPunct(token[hi-1])) hi--;

                var core = (hi > lo) ? token.Substring(lo, hi - lo) : token;
                // username se contém '_' (ex.: "Linck_") ou já foi visto no
                // container "Names" (cobre nome sem '_' como "xow")
                if (core.Length < 2 || (!core.Contains('_') && !KnownPlayerNames.Contains(core)))
                {
                    sb.Append(token); continue;
                }

                var ph = caps.Count == 0 ? "{player}" : $"{{player{caps.Count + 1}}}";
                if (lo > 0) sb.Append(token, 0, lo);          // pontuação líder
                sb.Append(ph);
                if (hi < token.Length) sb.Append(token, hi, token.Length - hi); // pontuação final
                caps.Add(core);
            }

            if (caps.Count == 0) return s;
            captured = caps.ToArray();
            return sb.ToString();
        }

        // substitui nome de NPC conhecido (KnownNpcNames/npc_names.json) por
        // {npc}, {npc2}... pra uma só entrada de tradução cobrir todos os
        // NPCs: "Talk to Booker..." -> "Talk to {npc}...", com "{npc}" =
        // "Bibliotecário" no JSON, restaurado em runtime.
        internal static string NormalizeNpcNames(string s, out string[] capturedNpcs)
        {
            capturedNpcs = Array.Empty<string>();
            if (KnownNpcNames.Count == 0 || string.IsNullOrEmpty(s)) return s;

            var caps = new List<string>();
            var sb   = new StringBuilder(s.Length);
            int i    = 0;

            while (i < s.Length)
            {
                if (!char.IsLetter(s[i])) { sb.Append(s[i++]); continue; }

                int wStart = i;
                while (i < s.Length && char.IsLetter(s[i])) i++;
                var word = s.Substring(wStart, i - wStart);

                if (KnownNpcNames.ContainsKey(word))
                {
                    var ph = caps.Count == 0 ? "{npc}" : $"{{npc{caps.Count + 1}}}";
                    caps.Add(word);
                    sb.Append(ph);
                }
                else
                {
                    sb.Append(word);
                }
            }

            if (caps.Count == 0) return s;
            capturedNpcs = caps.ToArray();
            return sb.ToString();
        }

        internal static string RestoreNpcNames(string result, string[] capturedNpcs)
        {
            if (capturedNpcs == null || capturedNpcs.Length == 0) return result;
            for (int i = 0; i < capturedNpcs.Length; i++)
            {
                var ph       = i == 0 ? "{npc}" : $"{{npc{i + 1}}}";
                var restored = KnownNpcNames.TryGetValue(capturedNpcs[i], out var trans)
                               && !string.IsNullOrEmpty(trans)
                               ? trans : capturedNpcs[i];
                result = result.Replace(ph, restored);
            }
            return result;
        }

        // aplicada depois de NormalizeNumbers — detecta palavra minúscula que
        // precede um placeholder "-{n\d+}" e substitui por {map}, {map2}...
        // Ex.: "{n1} players in intro-{n2}" -> "{n1} players in {map}-{n2}",
        // capturedMaps = ["intro"]. Permite uma só tradução pra todos os mapas.
        internal static string NormalizeMaps(string s, out string[] capturedMaps)
        {
            capturedMaps = Array.Empty<string>();
            if (string.IsNullOrEmpty(s)) return s;

            const string Prefix = "-{n"; // marcador de início do placeholder de número
            if (!s.Contains(Prefix)) return s;

            var caps = new List<string>();
            var sb   = new StringBuilder(s.Length);
            int i    = 0;

            while (i < s.Length)
            {
                int dash = s.IndexOf(Prefix, i, StringComparison.Ordinal);
                if (dash < 0) { sb.Append(s, i, s.Length - i); break; }

                // confere se depois de "{n" vem um dígito (evita "-{npc}" etc.)
                int digitPos = dash + Prefix.Length;
                if (digitPos >= s.Length || !char.IsDigit(s[digitPos]))
                {
                    sb.Append(s, i, dash - i + 1);
                    i = dash + 1;
                    continue;
                }

                // lê pra trás a palavra em minúsculas antes do '-'
                int wordEnd   = dash;           // exclusivo
                int wordStart = wordEnd - 1;
                while (wordStart >= 0 && char.IsLower(s[wordStart])) wordStart--;
                wordStart++;                    // inclusivo

                if (wordStart >= wordEnd)
                {
                    // nenhuma palavra minúscula antes do traço
                    sb.Append(s, i, dash - i + 1);
                    i = dash + 1;
                    continue;
                }

                string mapName = s.Substring(wordStart, wordEnd - wordStart);
                var ph = caps.Count == 0 ? "{map}" : $"{{map{caps.Count + 1}}}";
                caps.Add(mapName);

                sb.Append(s, i, wordStart - i); // texto até o início da palavra
                sb.Append(ph);                  // placeholder no lugar do nome de zona
                sb.Append(s[dash]);             // preserva o '-'
                // avança pra logo depois do '-', não pra "wordEnd" (o próprio
                // índice de '-') — senão o próximo IndexOf acha o MESMO "-{n"
                // de novo e reprocessa a palavra, gerando count negativo no
                // Append seguinte (ArgumentOutOfRangeException). Bug real
                // achado via teste automatizado — nunca visível ao usuário
                // porque DoTranslate/TranslateRaw têm catch(Exception) amplo
                // que engolia o erro e devolvia o texto original sem traduzir.
                i = dash + 1;
            }

            if (caps.Count == 0) return s;
            capturedMaps = caps.ToArray();
            return sb.ToString();
        }

        internal static string RestoreMaps(string result, string[] capturedMaps)
        {
            if (capturedMaps == null || capturedMaps.Length == 0) return result;
            for (int i = 0; i < capturedMaps.Length; i++)
            {
                var ph = i == 0 ? "{map}" : $"{{map{i + 1}}}";
                result = result.Replace(ph, capturedMaps[i]);
            }
            return result;
        }

        // template de navegação de quest, formato fixo do jogo:
        // "Goto map Intro to continue the quest \"Continue Journey\""
        // vira a chave: Goto map {map} to continue the quest "{quest}"
        // {map} nunca traduz, é restaurado literal. {quest} (sempre entre
        // aspas) traduz se houver entrada no JSON, senão mantém original.
        // Retorna true e preenche key/map/quest quando o texto casa o formato.
        private const string QGotoPrefix = "Goto map ";
        private const string QGotoMiddle = " to continue the quest \"";

        internal static bool TryNormalizeQuestGoto(
            string s, out string key, out string map, out string quest)
        {
            key = ""; map = ""; quest = "";
            if (string.IsNullOrEmpty(s) ||
                !s.StartsWith(QGotoPrefix, StringComparison.Ordinal))
                return false;

            int mid = s.IndexOf(QGotoMiddle, QGotoPrefix.Length, StringComparison.Ordinal);
            if (mid <= QGotoPrefix.Length) return false; // mapa não pode ser vazio

            map = s.Substring(QGotoPrefix.Length, mid - QGotoPrefix.Length).Trim();
            if (map.Length == 0) return false;

            int qStart = mid + QGotoMiddle.Length;
            int qEnd   = s.IndexOf('"', qStart);
            if (qEnd < 0) return false;

            quest = s.Substring(qStart, qEnd - qStart).Trim();
            if (quest.Length == 0) return false;

            // preserva sufixo após a aspa final (normalmente vazio)
            string suffix = s.Substring(qEnd + 1);
            key = QGotoPrefix + "{map}" + QGotoMiddle + "{quest}\"" + suffix;
            return true;
        }

        /// <summary>
        /// Restaura os valores reais de {player}, {player2}, … numa string traduzida.
        /// </summary>
        internal static string RestorePlaceholders(string translated, string[] captured)
        {
            if (captured == null || captured.Length == 0) return translated;
            for (int i = 0; i < captured.Length; i++)
            {
                var ph = i == 0 ? "{player}" : $"{{player{i + 1}}}";
                translated = translated.Replace(ph, captured[i]);
            }
            return translated;
        }

        // pontuação que pode envolver um token sem fazer parte do username
        private static bool IsTokenPunct(char c) =>
            c == '.' || c == '!' || c == '?' || c == ',' || c == ';' || c == ':';

        private static string StripClone(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith("(Clone)", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - 7).TrimEnd()
                : name;
        }
    }
}
