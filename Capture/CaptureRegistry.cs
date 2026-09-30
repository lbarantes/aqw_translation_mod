using System;
using System.Collections.Generic;
using System.Linq;

namespace AQWMod.Localization.Capture
{
    // Guarda todo texto já observado pelo mod (push do Harmony + pull do scan
    // do overlay), num único dicionário por chave normalizada.
    //
    // É append/update-only de propósito: só Del/Limpar (ação explícita do
    // usuário) remove uma entrada. Scan e refresh nunca removem — é o que
    // evita aquele bug antigo de "Refresh limpa o modo Acumulado".
    //
    // Guardamos as strings, não os componentes Unity, então texto transiente
    // (cutscene, diálogo que some rápido) sobrevive ao GC e à troca de cena.
    //
    // Lock simples porque as escritas já vêm do main thread, só por garantia
    // caso algum patch dispare fora dele.
    public static class CaptureRegistry
    {
        public sealed class Entry
        {
            public string   Key         = "";                     // chave normalizada (dedup)
            public string   RawOriginal = "";                     // texto completo com markup
            public string   Category    = "";
            public string   Path        = "";
            public string[] PlayerCaps  = Array.Empty<string>();  // {player} capturados
            public string[] NumCaps     = Array.Empty<string>();  // {n1},{n2},… capturados
            public string[] MapCaps     = Array.Empty<string>();  // {map} capturados
            public float    FirstSeen;                            // Time.realtimeSinceStartup
            public float    LastSeen;
            public int      SeenCount;
            public string   Source      = "";                     // "tmp", "ui", "scan"
        }

        private static readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly object _gate = new object();

        public static int Count
        {
            get { lock (_gate) { return _entries.Count; } }
        }

        /// <summary>
        /// Registra (ou atualiza) um texto observado. Idempotente por chave.
        /// Chamado tanto pelo push do Harmony quanto pelo pull do scan do overlay.
        /// </summary>
        public static void Record(
            string key, string rawOriginal, string category, string path,
            string[]? players, string[]? nums, string[]? maps,
            float now, string source)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 2) return;

            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out var e))
                {
                    e = new Entry { Key = key, FirstSeen = now };
                    _entries[key] = e;
                }

                // só sobrescreve quando o novo valor traz algo — assim o pull do
                // scan (sem placeholders) não apaga o que o push do Harmony capturou
                if (!string.IsNullOrEmpty(rawOriginal)) e.RawOriginal = rawOriginal;
                if (!string.IsNullOrEmpty(category))    e.Category    = category;
                if (!string.IsNullOrEmpty(path))        e.Path        = path;
                if (players != null && players.Length > 0) e.PlayerCaps = players;
                if (nums    != null && nums.Length    > 0) e.NumCaps    = nums;
                if (maps    != null && maps.Length    > 0) e.MapCaps    = maps;
                if (!string.IsNullOrEmpty(source))      e.Source       = source;

                e.LastSeen = now;
                e.SeenCount++;
            }
        }

        /// <summary>Todas as entradas já observadas (modo Acumulado).</summary>
        public static List<Entry> Snapshot()
        {
            lock (_gate) { return _entries.Values.ToList(); }
        }

        /// <summary>
        /// Entradas vistas dentro da janela de tempo (modo "tela atual").
        /// O scan ativo carimba LastSeen=now nos textos visíveis, então eles
        /// sempre passam; textos transientes recém-empurrados também aparecem
        /// por alguns segundos.
        /// </summary>
        public static List<Entry> RecentlySeen(float now, float windowSeconds)
        {
            lock (_gate)
            {
                return _entries.Values
                    .Where(e => now - e.LastSeen <= windowSeconds)
                    .ToList();
            }
        }

        /// <summary>Remoção explícita por ação do usuário (botão Del). NÃO usado por scan/refresh.</summary>
        public static bool Remove(string key)
        {
            lock (_gate) { return _entries.Remove(key); }
        }

        /// <summary>Limpeza total explícita por ação do usuário (botão Limpar). NÃO usado por scan/refresh.</summary>
        public static void Clear()
        {
            lock (_gate) { _entries.Clear(); }
        }
    }
}
