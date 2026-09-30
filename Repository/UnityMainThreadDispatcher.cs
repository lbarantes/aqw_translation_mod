using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace AQWMod.Localization.Repository
{
    // Executa callbacks enfileirados no main thread — necessário porque
    // FileSystemWatcher dispara em threads do pool, e Unity API/nosso
    // cache só podem ser tocados no main thread. MonoBehaviour com
    // DontDestroyOnLoad sobrevive à troca de cena; ConcurrentQueue permite
    // enqueue de qualquer thread sem lock; Update() drena com budget por frame.
    public sealed class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private static UnityMainThreadDispatcher? _instance;

        // máximo de ações por frame, pra não gerar spike de frame time
        private const int MaxActionsPerFrame = 5;

        /// <summary>
        /// Enfileira uma ação para execução no próximo Update do main thread.
        /// Thread-safe.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        public static void Initialize()
        {
            if (_instance != null) return;

            var go = new GameObject("[AQWMod.MainThreadDispatcher]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
        }

        private void Update()
        {
            int budget = MaxActionsPerFrame;
            while (budget-- > 0 && _queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex)
                {
                    Debug.TranslationLogger.StaticError(
                        $"[Dispatcher] Exceção em action enfileirada: {ex.Message}");
                }
            }
        }

        private void OnDestroy()
        {
            _instance = null;
        }
    }
}
