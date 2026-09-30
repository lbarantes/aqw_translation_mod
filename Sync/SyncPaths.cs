using System;
using System.Linq;

namespace AQWMod.Localization.Sync
{
    // Regras de quais caminhos relativos (sempre com '/') um pacote remoto pode
    // trazer. Compartilhado pela validação do manifesto, da extração do ZIP e do
    // SyncEngine — a mesma regra nos três, pra nenhuma camada aceitar o que a
    // outra recusaria.
    internal static class SyncPaths
    {
        /// <summary>null = caminho aceito; senão o motivo da recusa.</summary>
        public static string? RejectReason(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return "caminho vazio";
            if (rel.IndexOf('\\') >= 0) return "caminho com barra invertida";
            if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "não é .json";

            var parts = rel.Split('/');
            if (parts.Any(s => s == ".." || s == "." || s.Length == 0 || s.IndexOf(':') >= 0))
                return "caminho inválido";
            if (parts.Any(s => s.StartsWith("_", StringComparison.Ordinal)))
                return "pasta/arquivo reservado (começa com '_')";
            if (string.Equals(parts[0], "overrides", StringComparison.OrdinalIgnoreCase))
                return "o pacote remoto não pode escrever na camada overrides/";
            if (parts.Length == 1 &&
                (string.Equals(parts[0], "uncategorized.json", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "npc_names.json", StringComparison.OrdinalIgnoreCase)))
                return "arquivo reservado da raiz";
            return null;
        }
    }
}
