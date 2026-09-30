using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using AQWMod.Localization.Core;
using AQWMod.Localization.Interceptors;
using AQWMod.Localization.Overlay;
using AQWMod.Localization.Repository;

namespace AQWMod.Localization
{
    // Entry point BepInEx do sistema de localização AQW PT-BR. Cada patch é
    // registrado individualmente em try/catch — falha de um overload não
    // aborta os outros, e o log diz quais aplicaram e quais falharam.
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInProcess("AdventureQuest Worlds Infinity.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; } = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Log = Logger;

            try
            {
                ModConfig.Initialize(Config);

                if (!ModConfig.Enabled.Value)
                {
                    Logger.LogInfo("[AQWTranslation] Mod desativado via config.");
                    return;
                }

                UnityMainThreadDispatcher.Initialize();

                var translationsPath = ResolveTranslationsPath();
                Logger.LogInfo($"[AQWTranslation] Pasta de traduções: '{translationsPath}'");
                TranslationManager.Instance.Initialize(translationsPath);
                TMPTextPatch.LoadNpcNames(translationsPath); // npc_names.json (opcional)

                // TranslationManager não conhece TMPTextPatch diretamente (evita
                // dependência circular Core -> Interceptors), então quem recarrega
                // npc_names.json é o Plugin, que já referencia os dois
                TranslationManager.Instance.OnNpcNamesFileChanged += () =>
                {
                    TMPTextPatch.LoadNpcNames(translationsPath);
                    TMPTextPatch.InvalidateSafeTranslateCache();
                    Logger.LogInfo("[AQWTranslation] npc_names.json recarregado.");
                };

                _harmony = new Harmony(PluginInfo.GUID);
                RegisterPatches();

                // overlay de edição in-game (F10 pra abrir). O prefixo "_AQW" é
                // obrigatório: a varredura e o inspector ignoram qualquer objeto
                // cujo transform.root.name comece com "_AQW", pra não capturar/
                // inspecionar a própria overlay — como o canvas é filho deste
                // GameObject, é o nome dele que vira o root de toda a UI
                var overlayGo = new GameObject("_AQWTranslationEditorOverlay");
                overlayGo.AddComponent<TranslationEditorOverlay>();
                DontDestroyOnLoad(overlayGo);
                Logger.LogInfo("[AQWTranslation] Overlay pronto — pressione F10 no jogo.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AQWTranslation] Falha crítica na inicialização: {ex}");
                Logger.LogError("[AQWTranslation] O jogo continua sem tradução.");
            }
        }

        private void RegisterPatches()
        {
            int ok = 0, total = 0;
            void Try(MethodBase? method, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, string label = "")
            {
                total++;
                ok += TryPatch(method, prefix, postfix, label);
            }

            // setter .text — mais importante, cobre a maioria dos casos
            Try(AccessTools.PropertySetter(typeof(TMP_Text), "text"),
                prefix: new HarmonyMethod(typeof(TMPTextPatch), nameof(TMPTextPatch.Prefix_TextSetter)),
                label: "TMP_Text.set_text");

            // overloads de SetText — Postfix, pra evitar bind de ref em método abstrato
            Try(AccessTools.Method(typeof(TMP_Text), "SetText", new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(TMPTextPatch), nameof(TMPTextPatch.Postfix_SetText_String)),
                label: "TMP_Text.SetText(string)");

            Try(AccessTools.Method(typeof(TMP_Text), "SetText", new[] { typeof(string), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(TMPTextPatch), nameof(TMPTextPatch.Postfix_SetText_StringBool)),
                label: "TMP_Text.SetText(string, bool)");

            Try(AccessTools.Method(typeof(TMP_Text), "SetText",
                    new[] { typeof(string), typeof(float), typeof(float), typeof(float) }),
                postfix: new HarmonyMethod(typeof(TMPTextPatch), nameof(TMPTextPatch.Postfix_SetText_StringFloats)),
                label: "TMP_Text.SetText(string, float, float, float)");

            // OnEnable cobre texto serializado pelo Unity (não passa pelo setter).
            // Patcha as classes concretas, não a TMP_Text abstrata — TMP_Text herda
            // OnEnable de MaskableGraphic, então patchar a abstrata afetaria todo
            // elemento de UI (Image etc.) e causaria crash.
            Try(AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable"),
                postfix: new HarmonyMethod(typeof(TMPTextPatch), nameof(TMPTextPatch.Postfix_OnEnable)),
                label: "TextMeshProUGUI.OnEnable");

            Try(AccessTools.Method(typeof(TextMeshPro), "OnEnable"),
                postfix: new HarmonyMethod(typeof(TMPTextPatch), nameof(TMPTextPatch.Postfix_OnEnable)),
                label: "TextMeshPro.OnEnable");

            // UnityEngine.UI.Text (legado) — antes deste patch, UI.Text só era
            // traduzido pela varredura periódica do overlay (até ~1s de atraso)
            Try(AccessTools.PropertySetter(typeof(UnityEngine.UI.Text), "text"),
                prefix: new HarmonyMethod(typeof(LegacyTextPatch), nameof(LegacyTextPatch.Prefix_TextSetter)),
                label: "UI.Text.set_text");

            try
            {
                _harmony!.PatchAll(typeof(InputBlockerPatch));
                Logger.LogInfo("[AQWTranslation] ✓ Input blocker ativado");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[AQWTranslation] ✗ Input blocker falhou: {ex.Message}");
            }

            try
            {
                _harmony!.PatchAll(typeof(InputFieldCaretFix));
                Logger.LogInfo("[AQWTranslation] ✓ InputField caret fix ativado");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[AQWTranslation] ✗ InputField caret fix falhou: {ex.Message}");
            }

            int fail = total - ok;
            Logger.LogInfo($"[AQWTranslation] Patches: {ok} OK" +
                           (fail > 0 ? $", {fail} falhou (veja log acima)" : "") +
                           $" | {TranslationManager.Instance.Repository.Count} strings carregadas");
        }

        private int TryPatch(MethodBase? method,
            HarmonyMethod? prefix  = null,
            HarmonyMethod? postfix = null,
            string label = "")
        {
            if (method == null)
            {
                Logger.LogWarning($"[AQWTranslation] Método não encontrado: {label}");
                return 0;
            }
            try
            {
                _harmony!.Patch(method, prefix: prefix, postfix: postfix);
                Logger.LogInfo($"[AQWTranslation] ✓ Patcheado: {label}");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[AQWTranslation] ✗ Falhou: {label} — {ex.Message}");
                return 0;
            }
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
                TranslationManager.Instance.Shutdown();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AQWTranslation] Erro no shutdown: {ex.Message}");
            }
        }

        // suporta múltiplos layouts de instalação: A) plugins\AQWTranslation\
        // translations\ (recomendado), B) plugins\translations\ (legado), C)
        // ao lado do DLL\translations\
        private static string ResolveTranslationsPath()
        {
            var dllDir     = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                             ?? string.Empty;
            var pluginsDir = Paths.PluginPath;

            var candidates = new[]
            {
                Path.Combine(pluginsDir, "translations"),                  // B: legado
                Path.Combine(dllDir,     "translations"),                  // A: subpasta do DLL
                Path.Combine(pluginsDir, "AQWTranslation", "translations"),// C: explícito
            };

            foreach (var c in candidates)
            {
                if (Directory.Exists(c) && HasJsonFiles(c))
                    return c;
            }

            // Nenhum encontrado — cria o path padrão
            var defaultPath = candidates[0];
            Directory.CreateDirectory(defaultPath);
            Log.LogWarning($"[AQWTranslation] Pasta de traduções não encontrada. " +
                           $"Criada em: '{defaultPath}'");
            return defaultPath;
        }

        private static bool HasJsonFiles(string path)
        {
            try { return Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Length > 0; }
            catch { return false; }
        }
    }

    internal static class PluginInfo
    {
        public const string GUID    = "com.aqwmod.localization";
        public const string Name    = "AQW PT-BR Localization";
        public const string Version = "2.2.0";
    }
}
