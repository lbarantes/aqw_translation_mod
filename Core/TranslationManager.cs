using System;
using AQWMod.Localization.Repository;
using AQWMod.Localization.Pipeline;
using AQWMod.Localization.Capture;
using AQWMod.Localization.Debug;

namespace AQWMod.Localization.Core
{
    // Singleton central do sistema de localização: inicializa os subsistemas,
    // expõe Translate() (usado pelos patches Harmony) e coordena hot reload.
    //
    // Translate() só é chamado no main thread da Unity (patches de UI), e o
    // HotReload é marshalled pro main thread via UnityMainThreadDispatcher —
    // então não precisa de lock aqui.
    public sealed class TranslationManager
    {
        private static readonly Lazy<TranslationManager> _instance =
            new Lazy<TranslationManager>(() => new TranslationManager());

        public static TranslationManager Instance => _instance.Value;

        public  ITranslationRepository     Repository          { get; private set; } = null!;
        public  string                     TranslationsRootPath { get; private set; } = string.Empty;
        private ILocalizationPipeline      _pipeline   = null!;
        private IMissingTranslationCollector _collector = null!;
        private TranslationCache           _cache      = null!;
        private HotReloadWatcher           _watcher    = null!;
        private TranslationLogger          _logger     = null!;
        private TranslationStats           _stats      = null!;

        private bool _initialized = false;

        /// <summary>Disparado no main thread após qualquer hot reload de tradução.</summary>
        public event Action? OnTranslationsReloaded;

        /// <summary>
        /// Disparado no main thread quando npc_names.json muda. TranslationManager
        /// não conhece TMPTextPatch diretamente (evitaria uma dependência circular
        /// Core -> Interceptors) — quem sabe recarregar o arquivo (Plugin.cs, que
        /// já referencia os dois) escuta este evento.
        /// </summary>
        public event Action? OnNpcNamesFileChanged;

        private TranslationManager() { }

        public void Initialize(string translationsRootPath)
        {
            if (_initialized) return;

            TranslationsRootPath = translationsRootPath;
            _logger = new TranslationLogger();
            _stats  = new TranslationStats();
            _cache  = new TranslationCache(ModConfig.CacheCapacity.Value);

            var repo = new TranslationRepository(_logger);
            Repository = repo;
            repo.LoadAll(translationsRootPath);

            _pipeline  = new LocalizationPipeline(Repository, _logger);
            _collector = new MissingTranslationCollector(translationsRootPath, _logger);

            if (ModConfig.HotReloadEnabled.Value)
            {
                _watcher = new HotReloadWatcher(
                    translationsRootPath,
                    ModConfig.HotReloadDebounce.Value);
                _watcher.OnFileChanged += HandleHotReload;
                _watcher.Start();
            }

            _initialized = true;
            _logger.Info($"Inicializado. {Repository.Count} strings carregadas de '{translationsRootPath}'.");
        }

        /// <summary>
        /// Traduz uma string. Chamado exclusivamente pelo TextInterceptor (Harmony patch).
        /// Hotpath — precisa ser O(1) no cache hit.
        /// </summary>
        public string Translate(string original, TranslationContext? context = null)
        {
            if (!_initialized || !ModConfig.Enabled.Value) return original;
            if (string.IsNullOrWhiteSpace(original))       return original;
            if (original.Length < 2)                        return original;

            _stats.IncrementRequests();

            var cacheKey = HashUtils.Fnv1a64(original);
            if (_cache.TryGet(cacheKey, out var cached))
            {
                _stats.IncrementCacheHits();
                return cached!;
            }

            var ctx    = context ?? TranslationContext.Simple(original);
            var result = _pipeline.Process(ctx);

            if (result.IsTranslated)
            {
                // no JSON uma quebra de linha real é "\n" (uma barra), mas editores
                // costumam gravar "\\n" (duas), que o parser entrega como o texto
                // literal "\n" — o jogo mostraria isso em vez de quebrar a linha.
                // Converte aqui, só no texto já traduzido, pra aceitar os dois jeitos.
                var finalText = UnescapeLineBreaks(result.Text);

                _cache.Set(cacheKey, finalText);
                _stats.IncrementHits();

                if (ModConfig.VerboseLogging.Value)
                    _logger.Debug($"'{original}' → '{finalText}'");

                return finalText;
            }

            _stats.IncrementMisses();

            if (ModConfig.LogMisses.Value)
                _logger.Debug($"[MISS] '{original}'");

            if (ModConfig.CaptureEnabled.Value)
                _collector.Capture(original, ctx);

            return result.Text;
        }

        // "\r\n"/"\n"/"\r" literais -> quebra de linha real; "\t" literal -> tab real.
        // Sai cedo se não tiver barra invertida nenhuma.
        private static string UnescapeLineBreaks(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            return s.Replace("\\r\\n", "\n")
                    .Replace("\\n",     "\n")
                    .Replace("\\r",     "\n")
                    .Replace("\\t",     "\t");
        }

        /// <summary>
        /// Recarrega um arquivo JSON específico e invalida o cache interno.
        /// Usado pelo overlay ao salvar, pra forçar reload imediato mesmo com
        /// HotReloadEnabled = false.
        /// </summary>
        public void ReloadFile(string filePath) => HandleHotReload(filePath);

        private void HandleHotReload(string filePath)
        {
            // npc_names.json não é arquivo de tradução, é a tabela de nomes de
            // NPC usada pelo placeholder {npc} do TMPTextPatch. Não passa pelo
            // TranslationRepository.ReloadFile (já é excluído do load normal) —
            // só invalida o cache (nomes de NPC afetam textos já traduzidos) e
            // avisa quem sabe recarregar o arquivo em si.
            if (IsNpcNamesFile(filePath))
            {
                _logger.Info("[HotReload] npc_names.json alterado.");
                _cache.Invalidate();
                OnNpcNamesFileChanged?.Invoke();
                return;
            }

            _logger.Info($"[HotReload] Recarregando: {System.IO.Path.GetFileName(filePath)}");

            try
            {
                ((TranslationRepository)Repository).ReloadFile(filePath);
                _cache.Invalidate();
                _stats.IncrementReloads();
                OnTranslationsReloaded?.Invoke();
                _logger.Info($"[HotReload] OK. Cache invalidado. {Repository.Count} strings ativas.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[HotReload] Falha ao recarregar '{filePath}': {ex.Message}");
            }
        }

        private bool IsNpcNamesFile(string filePath)
        {
            if (!string.Equals(System.IO.Path.GetFileName(filePath), "npc_names.json",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            var dir = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty;
            try
            {
                return string.Equals(
                    System.IO.Path.GetFullPath(dir).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    System.IO.Path.GetFullPath(TranslationsRootPath).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void Shutdown()
        {
            _watcher?.Stop();

            _collector?.FlushToDisk();

            if (ModConfig.ShowStatsOnExit.Value)
                _stats.DumpToLog(_logger);
        }

        public TranslationStats Stats => _stats;
    }
}
