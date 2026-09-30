using System.Text;
using AQWMod.Localization.Interceptors;

namespace AQWMod.Localization.Overlay
{
    // Monta o texto extra (linhas "{player} = ...") anexado ao caminho de uma
    // linha do Scan, mostrando os valores reais por trás de cada placeholder.
    internal static class PlaceholderHintFormatter
    {
        public static string Append(string path,
            string[] players, string[] npcs, string[] nums, string[] maps)
        {
            if (players.Length == 0 && npcs.Length == 0 && nums.Length == 0 && maps.Length == 0)
                return path;

            var sb = new StringBuilder(path);

            for (int i = 0; i < players.Length; i++)
            {
                var ph = i == 0 ? "player" : $"player{i + 1}";
                sb.Append($"\n{{{ph}}} = \"{players[i]}\"");
            }

            for (int i = 0; i < npcs.Length; i++)
            {
                var ph = i == 0 ? "npc" : $"npc{i + 1}";
                // mostra nome original e tradução, se diferente
                var trans = TMPTextPatch.KnownNpcNames.TryGetValue(npcs[i], out var t) ? t : npcs[i];
                var hint  = trans != npcs[i] ? $"\"{npcs[i]}\" → \"{trans}\"" : $"\"{npcs[i]}\"";
                sb.Append($"\n{{{ph}}} = {hint}");
            }

            for (int i = 0; i < nums.Length; i++)
                sb.Append($"\n{{n{i + 1}}} = {nums[i]}");

            for (int i = 0; i < maps.Length; i++)
            {
                var ph = i == 0 ? "map" : $"map{i + 1}";
                sb.Append($"\n{{{ph}}} = \"{maps[i]}\"");
            }

            return sb.ToString();
        }
    }
}
