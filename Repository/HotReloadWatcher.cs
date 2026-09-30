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

            // ignora os próprios arquivos _capture gerados pelo mod
            if (Path.GetFileName(path).StartsWith("_")) return;
            if (path.Contains(Path.DirectorySeparatorChar + "_capture" + Path.DirectorySeparatorChar)) return;

            lock (_debounceLock)
            {
                if (_lastEvent.TryGetValue(path, out var last) &&
                    (now - last).TotalMilliseconds < _debounceMs)
                    return;

                _lastEvent[path] = now;
            }

            UnityMainThreadDispatcher.Enqueue(() => OnFileChanged?.Invoke(path));
        }

        public void Dispose() => Stop();
    }
}
