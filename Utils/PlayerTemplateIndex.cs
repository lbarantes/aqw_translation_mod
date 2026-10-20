using System;
using System.Collections.Generic;
using System.Text;

namespace AQWMod.Localization.Utils
{
    // Casa um texto do jogo contra as chaves de tradução que têm {player}.
    //
    // A normalização de {player} do TMPTextPatch só reconhece nome com '_'
    // ou nome já visto num nameplate do mapa. Nome de outro jogador sem '_'
    // que nunca apareceu no mapa (ex.: "pittureca has invited you to join a
    // party!") passava batido. Aqui é o contrário: a chave cadastrada
    // ("{player} has invited you to join a party!") vira um molde, e o texto
    // que encaixa no molde tem o nome capturado do lugar do {player}.
    //
    // Slots reconhecidos no molde:
    //   {player}, {player2}…  qualquer texto de 1 a MaxNameLength chars, sem quebra de linha
    //   {n1}, {n2}…           sequência de dígitos
    //   {map}, {map2}…        sequência de letras minúsculas
    // Qualquer outra coisa (inclusive {npc}, {quest}) é texto literal.
    public sealed class PlayerTemplateIndex
    {
        internal const int MaxNameLength = 32;
        // letras fora dos slots — sem isso "{player}!" casaria com qualquer exclamação
        private const int MinLiteralLetters = 3;

        private enum SlotKind { None, Player, Number, Map }

        private sealed class Part
        {
            public string   Literal = "";
            public SlotKind Kind;
            public string   Slot    = ""; // "player2", "n1"...
        }

        private sealed class Template
        {
            public string Key    = "";
            public Part[] Parts  = Array.Empty<Part>();
            public string Prefix = ""; // literal inicial, filtro rápido
            public string Suffix = ""; // literal final, filtro rápido
        }

        public readonly struct Match
        {
            public string TemplateKey { get; }
            public IReadOnlyDictionary<string, string> Slots { get; }

            public Match(string templateKey, IReadOnlyDictionary<string, string> slots)
            {
                TemplateKey = templateKey;
                Slots       = slots;
            }

            /// <summary>Valores de {player}, {player2}… na ordem do índice (buraco = "").</summary>
            public string[] Players => Ordered("player");

            /// <summary>Valores de {n1}, {n2}… na ordem do índice (buraco = "").</summary>
            public string[] Numbers => Ordered("n");

            /// <summary>Troca cada slot capturado pelo valor real numa string (a tradução).</summary>
            public string Fill(string translated)
            {
                foreach (var kv in Slots)
                    translated = translated.Replace("{" + kv.Key + "}", kv.Value);
                return translated;
            }

            private string[] Ordered(string baseName)
            {
                int max = 0;
                foreach (var name in Slots.Keys)
                    if (TrySlotIndex(name, baseName, out var idx) && idx > max) max = idx;
                if (max == 0) return Array.Empty<string>();

                var arr = new string[max];
                for (int i = 0; i < max; i++) arr[i] = "";
                foreach (var kv in Slots)
                    if (TrySlotIndex(kv.Key, baseName, out var idx)) arr[idx - 1] = kv.Value;
                return arr;
            }

            // "player" -> 1, "player3" -> 3, "n2" -> 2 ("n" sozinho não existe)
            private static bool TrySlotIndex(string name, string baseName, out int idx)
            {
                idx = 0;
                if (!name.StartsWith(baseName, StringComparison.Ordinal)) return false;
                var rest = name.Substring(baseName.Length);
                if (rest.Length == 0) { idx = baseName == "player" || baseName == "map" ? 1 : 0; return idx > 0; }
                return int.TryParse(rest, out idx) && idx > 0;
            }
        }

        private List<Template> _templates = new List<Template>();

        public int Count => _templates.Count;

        /// <summary>
        /// Remonta a lista a partir das chaves carregadas. Chaves com
        /// "@@contexto" entram pela parte de texto; as sem {player} são ignoradas.
        /// </summary>
        public void Rebuild(IEnumerable<string> keys)
        {
            var list = new List<Template>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in keys)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var sep = raw.IndexOf("@@", StringComparison.Ordinal);
                var key = sep >= 0 ? raw.Substring(0, sep) : raw;
                if (key.IndexOf("{player", StringComparison.Ordinal) < 0) continue;
                if (!seen.Add(key)) continue;

                var t = Parse(key);
                if (t != null) list.Add(t);
            }

            // molde com mais texto fixo primeiro: é o mais específico, e
            // ganha de um molde genérico que também encaixaria
            list.Sort((a, b) => LiteralLength(b).CompareTo(LiteralLength(a)));
            _templates = list;
        }

        /// <summary>
        /// Primeiro molde em que o texto encaixa. Só devolve true se pelo
        /// menos um {player} capturou um nome.
        /// </summary>
        public bool TryMatch(string text, out Match match)
        {
            match = default;
            if (string.IsNullOrEmpty(text)) return false;

            var templates = _templates; // Rebuild troca a referência inteira
            foreach (var t in templates)
            {
                if (t.Prefix.Length > 0 && !text.StartsWith(t.Prefix, StringComparison.Ordinal)) continue;
                if (t.Suffix.Length > 0 && !text.EndsWith(t.Suffix, StringComparison.Ordinal)) continue;

                var slots = new Dictionary<string, string>(StringComparer.Ordinal);
                if (MatchFrom(t.Parts, 0, text, 0, slots))
                {
                    match = new Match(t.Key, slots);
                    return true;
                }
            }
            return false;
        }

        // ── parse ───────────────────────────────────────────────────────────

        private static Template? Parse(string key)
        {
            var parts   = new List<Part>();
            var literal = new StringBuilder();
            int i = 0, letters = 0;
            bool hasPlayer = false;

            while (i < key.Length)
            {
                if (key[i] == '{')
                {
                    int close = key.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        var name = key.Substring(i + 1, close - i - 1);
                        var kind = KindOf(name);
                        if (kind != SlotKind.None)
                        {
                            // dois slots colados não têm fronteira pra separar
                            if (literal.Length == 0 && parts.Count > 0 &&
                                parts[parts.Count - 1].Kind != SlotKind.None)
                                return null;

                            if (literal.Length > 0)
                            {
                                parts.Add(new Part { Literal = literal.ToString() });
                                literal.Clear();
                            }
                            parts.Add(new Part { Kind = kind, Slot = name });
                            if (kind == SlotKind.Player) hasPlayer = true;
                            i = close + 1;
                            continue;
                        }
                    }
                }

                if (char.IsLetter(key[i])) letters++;
                literal.Append(key[i++]);
            }
            if (literal.Length > 0) parts.Add(new Part { Literal = literal.ToString() });

            if (!hasPlayer || letters < MinLiteralLetters) return null;

            var arr = parts.ToArray();
            return new Template
            {
                Key    = key,
                Parts  = arr,
                Prefix = arr[0].Kind == SlotKind.None ? arr[0].Literal : "",
                Suffix = arr[arr.Length - 1].Kind == SlotKind.None ? arr[arr.Length - 1].Literal : "",
            };
        }

        private static SlotKind KindOf(string name)
        {
            if (IsIndexed(name, "player", allowBare: true)) return SlotKind.Player;
            if (IsIndexed(name, "map",    allowBare: true)) return SlotKind.Map;
            if (IsIndexed(name, "n",      allowBare: false)) return SlotKind.Number;
            return SlotKind.None;
        }

        private static bool IsIndexed(string name, string baseName, bool allowBare)
        {
            if (!name.StartsWith(baseName, StringComparison.Ordinal)) return false;
            if (name.Length == baseName.Length) return allowBare;
            for (int i = baseName.Length; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return false;
            return true;
        }

        private static int LiteralLength(Template t)
        {
            int n = 0;
            foreach (var p in t.Parts) if (p.Kind == SlotKind.None) n += p.Literal.Length;
            return n;
        }

        // ── match (backtracking; moldes são curtos, poucos slots) ─────────────

        private static bool MatchFrom(Part[] parts, int pi, string s, int si,
            Dictionary<string, string> slots)
        {
            if (pi == parts.Length) return si == s.Length;
            var p = parts[pi];

            if (p.Kind == SlotKind.None)
            {
                if (si + p.Literal.Length > s.Length) return false;
                if (string.CompareOrdinal(s, si, p.Literal, 0, p.Literal.Length) != 0) return false;
                return MatchFrom(parts, pi + 1, s, si + p.Literal.Length, slots);
            }

            // tamanhos possíveis pro slot, do menor pro maior (casamento preguiçoso)
            int maxLen = s.Length - si;
            if (p.Kind == SlotKind.Player && maxLen > MaxNameLength) maxLen = MaxNameLength;

            for (int len = 1; len <= maxLen; len++)
            {
                char c = s[si + len - 1];
                if (p.Kind == SlotKind.Number && !char.IsDigit(c)) break;
                if (p.Kind == SlotKind.Map    && !char.IsLower(c)) break;
                if (p.Kind == SlotKind.Player && (c == '\n' || c == '\r')) break;

                var value = s.Substring(si, len);
                // nome não começa nem termina com espaço
                if (p.Kind == SlotKind.Player && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[len - 1])))
                    continue;

                // mesmo slot repetido no molde precisa casar o mesmo valor
                if (slots.TryGetValue(p.Slot, out var prev))
                {
                    if (prev != value) continue;
                    if (MatchFrom(parts, pi + 1, s, si + len, slots)) return true;
                    continue;
                }

                slots[p.Slot] = value;
                if (MatchFrom(parts, pi + 1, s, si + len, slots)) return true;
                slots.Remove(p.Slot);
            }
            return false;
        }
    }
}
