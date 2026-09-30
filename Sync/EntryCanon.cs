using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    // Compara entradas de tradução pelo SIGNIFICADO, não pela grafia no JSON:
    // "Ataque" e { "text": "Ataque" } são a mesma entrada, a ordem das
    // propriedades não importa e "ignore": false equivale a ausência do campo.
    // Sem isso, reformatar um arquivo viraria "edição local" falsa no merge.
    internal static class EntryCanon
    {
        public static JToken Canon(JToken token)
        {
            if (token is not JObject obj) return token.DeepClone();

            var props = obj.Properties()
                .Where(p => !(p.Name == "ignore" &&
                              p.Value.Type == JTokenType.Boolean &&
                              !(bool)p.Value))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (props.Count == 1 && props[0].Name == "text" && props[0].Value.Type == JTokenType.String)
                return props[0].Value.DeepClone();

            return new JObject(props.Select(p => new JProperty(p.Name, p.Value.DeepClone())));
        }

        /// <summary>Ausente (null) só é igual a ausente.</summary>
        public static bool Same(JToken? a, JToken? b)
        {
            if (a == null || b == null) return a == null && b == null;
            return JToken.DeepEquals(Canon(a), Canon(b));
        }

        public static string? ContextOf(JToken token) =>
            token is JObject o && o["context"] is JToken c && c.Type == JTokenType.String
                ? (string?)c : null;
    }
}
