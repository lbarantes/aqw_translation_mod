using System.Collections.Generic;

namespace AQWMod.Localization.Core
{
    // Cache LRU com capacidade configurável. Dictionary + LinkedList pra manter
    // ordem de uso sem precisar ordenar nada. A chave é o hash FNV-1a da string
    // original em vez da string em si, pra não alocar à toa no hotpath.
    public sealed class TranslationCache
    {
        private readonly int _capacity;
        private readonly Dictionary<ulong, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lruList;
        private readonly object _lock = new object();

        private int _hits;
        private int _misses;

        public int Hits   => _hits;
        public int Misses => _misses;
        public int Size   => _map.Count;

        private struct CacheEntry
        {
            public ulong  Key;
            public string Value;
        }

        public TranslationCache(int capacity = 4096)
        {
            _capacity = capacity;
            _map      = new Dictionary<ulong, LinkedListNode<CacheEntry>>(capacity);
            _lruList  = new LinkedList<CacheEntry>();
        }

        public bool TryGet(ulong key, out string? value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    // manda pro topo, é o mais recente agora
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    _hits++;
                    return true;
                }
                value = null;
                _misses++;
                return false;
            }
        }

        public void Set(ulong key, string value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    // já existe, só atualiza e promove
                    _lruList.Remove(existing);
                    var updated = new LinkedListNode<CacheEntry>(
                        new CacheEntry { Key = key, Value = value });
                    _lruList.AddFirst(updated);
                    _map[key] = updated;
                    return;
                }

                // cheio, descarta o mais antigo
                if (_map.Count >= _capacity)
                {
                    var oldest = _lruList.Last!;
                    _lruList.RemoveLast();
                    _map.Remove(oldest.Value.Key);
                }

                var node = new LinkedListNode<CacheEntry>(
                    new CacheEntry { Key = key, Value = value });
                _lruList.AddFirst(node);
                _map[key] = node;
            }
        }

        public void Invalidate()
        {
            lock (_lock)
            {
                _map.Clear();
                _lruList.Clear();
            }
        }
    }
}
