using System;
using System.Collections.Generic;
using UnityEngine;

namespace AQWMod.Localization.Overlay
{
    // Resolve sprite real da UI do AQW (já carregado em memória pelo jogo)
    // pelo nome, pra reskinar o overlay sem desenhar arte nova.
    //
    // Resources.FindObjectsOfTypeAll<Sprite>() devolve todo Sprite já em
    // memória — não é uma busca no build inteiro, é um inventário do que o
    // jogo carregou até agora. Um nome pode não resolver se o jogador ainda
    // não passou por uma tela que usa aquele sprite; nesse caso Img()
    // (UIHelpers.cs) cai de volta pra cor lisa, sem erro nem log de ruído.
    //
    // Indexado sob demanda na primeira vez que a GUI abre, e cacheado pro
    // resto da sessão — FindObjectsOfTypeAll varre tudo, não é barato pra
    // chamar toda hora.
    internal static class GameUiAssets
    {
        private static Dictionary<string, Sprite>? _spritesByName;

        public static Sprite? TryGetSprite(string name)
        {
            EnsureIndexed();
            return _spritesByName!.TryGetValue(name, out var sprite) ? sprite : null;
        }

        /// <summary>
        /// Descarta o índice cacheado — a próxima <see cref="TryGetSprite"/>
        /// reconstrói do zero. Chamado sempre que o overlay reabre, pra dar
        /// uma chance de pegar sprite que o jogo carregou depois da última
        /// indexação (senão o índice ficaria congelado no que existia na
        /// primeira vez que a GUI foi montada).
        /// </summary>
        public static void Invalidate() => _spritesByName = null;

        private static void EnsureIndexed()
        {
            if (_spritesByName != null) return;
            _spritesByName = new Dictionary<string, Sprite>(StringComparer.Ordinal);

            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;

                // primeira ocorrência de cada nome vence — atlases diferentes
                // repetem nome (ex.: mais de um "Border"), sem contexto visual
                // pra saber qual é "o certo", e a ordem é determinística
                // dentro da mesma sessão
                if (!_spritesByName.ContainsKey(sprite.name))
                    _spritesByName[sprite.name] = sprite;
            }
        }
    }
}
