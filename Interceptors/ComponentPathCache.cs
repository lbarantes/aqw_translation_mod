using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace AQWMod.Localization.Interceptors
{
    public static class ComponentPathCache
    {
        private static readonly ConcurrentDictionary<int, string> _cache =
            new ConcurrentDictionary<int, string>();

        private static readonly ConcurrentDictionary<int, WeakReference<UnityEngine.Object>> _refs =
            new ConcurrentDictionary<int, WeakReference<UnityEngine.Object>>();

        private static int _callsSinceLastPurge = 0;
        private const  int PurgeInterval = 500;

        public static string GetOrBuild(Component component)
        {
            var id = component.GetInstanceID();
            if (_cache.TryGetValue(id, out var cached)) return cached;
            var path = BuildPath(component.transform);
            _cache[id] = path;
            _refs[id]  = new WeakReference<UnityEngine.Object>(component);
            if (++_callsSinceLastPurge >= PurgeInterval)
            {
                _callsSinceLastPurge = 0;
                PruneDestroyed();
            }
            return path;
        }

        private static string BuildPath(Transform t)
        {
            var parts   = new List<string>();
            var current = t;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void PruneDestroyed()
        {
            var toRemove = new List<int>();
            foreach (var kv in _refs)
            {
                if (!kv.Value.TryGetTarget(out var obj) || obj == null)
                    toRemove.Add(kv.Key);
            }
            foreach (var id in toRemove)
            {
                _cache.TryRemove(id, out _);
                _refs.TryRemove(id, out _);
            }
        }
    }
}
