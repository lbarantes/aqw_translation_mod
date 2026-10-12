using System;
using System.Collections.Generic;
using System.IO;

namespace AQWMod.Localization.Repository
{
    // Monitora a pasta de traduções com FileSystemWatcher. Dois problemas
    // conhecidos do FSW são tratados aqui: evento duplicado (editores salvam
    // em duas etapas — debounce por timestamp) e thread errada (FSW dispara
    // numa thread do pool — marshal pro main thread via UnityMainThreadDispatcher).
    public sealed class HotReloadWatcher : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private readonly string    _rootPath;
        private readonly int       _debounceMs;

        // último evento por caminho, pra debounce
        private readonly Dictionary<string, DateTime> _lastEvent =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly object _debounceLock = new object();

        /// <summary>
        /// Disparado no Unity main thread quando um arquivo JSON de tradução muda.
        /// </summary>
        public event Action<string>? OnFileChanged;

        public HotReloadWatcher(string rootPath, int debounceMs = 500)
        {
            _rootPath   = rootPath;
            _debounceMs = debounceMs;
        }

        public void Start()
        {
            if (!Directory.Exists(_rootPath))
            {
                Debug.TranslationLogger.StaticWarning(
                    $"[HotReload] Pasta não existe, watcher não iniciado: '{_rootPath}'");
                return;
            }

            _watcher = new FileSystemWatcher(_rootPath, "*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents   = true
            };

            _watcher.Changed += HandleFsEvent;
            _watcher.Created += HandleFsEvent;
            // gravacao atomica (temp -> Move/Replace) chega como Renamed, nao como
            // Created/Changed; arquivo apagado pelo atualizador chega como Deleted
            _watcher.Renamed += (s, e) => HandleFsEvent(s, e);
            _watcher.Deleted += HandleFsEvent;

            Debug.TranslationLogger.StaticWarning(
                $"[HotReload] Monitorando '{_rootPath}'");
        }

        public void Stop()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private void HandleFsEvent(object sender, FileSystemEventArgs e)
        {
            var path = e.FullPath;
            var now  = DateTime.UtcNow;

            // ignora o que o mod gera pra si (_capture, _sync, ...)
            if (ShouldIgnore(_rootPath, path)) return;

            lock (_debounceLock)
            {
                if (_lastEvent.TryGetValue(path, out var last) &&
                    (now - last).TotalMilliseconds < _debounceMs)
                    return;

                _lastEvent[path] = now;
            }

            UnityMainThreadDispatcher.Enqueue(() => OnFileChanged?.Invoke(path));
        }

        // Mesma regra do TranslationRepository.LoadAll: qualquer segmento do
        // caminho (pasta ou arquivo) começando com "_" é interno do mod.
        internal static bool ShouldIgnore(string rootPath, string fullPath)
        {
            var root = Path.GetFullPath(rootPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(fullPath);
            var rel  = full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(root.Length)
                : full;

            foreach (var seg in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (seg.StartsWith("_", StringComparison.Ordinal)) return true;
            return false;
        }

        public void Dispose() => Stop();
    }
}
