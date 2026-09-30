using System;
using System.IO;
using System.Linq;

namespace AQWMod.Localization.Overlay
{
    // Normaliza e valida o nome digitado no modal "Nova pasta"/"Novo arquivo"
    // da árvore, com a mesma checagem de path traversal de qualquer input
    // que vira caminho.
    internal static class NewNodeNameValidator
    {
        private static readonly char[] InvalidNameChars =
            Path.GetInvalidFileNameChars().Where(c => c != '/' && c != '\\').ToArray();

        /// <summary>
        /// Valida o nome de uma PASTA nova (pode conter subpastas, ex.:
        /// "npcs/quest") a ser criada dentro de <paramref name="parentRelPath"/>.
        /// Retorna null se válido (com <paramref name="normalizedRelativePath"/>
        /// preenchido), ou uma mensagem de erro pronta para exibir ao usuário.
        /// </summary>
        public static string? ValidateFolderPath(string? rawInput, string parentRelPath, out string normalizedRelativePath)
        {
            normalizedRelativePath = "";
            var name = (rawInput ?? "").Trim().Replace('\\', '/').Trim('/');

            if (string.IsNullOrEmpty(name))
                return "Informe um nome de pasta.";
            if (name.Contains("..") || Path.IsPathRooted(name))
                return "Nome inválido — use só o nome da pasta (ou subpasta/nome), sem \"..\".";
            if (name.Split('/').Any(seg => seg.Length == 0 || seg.IndexOfAny(InvalidNameChars) >= 0))
                return "Nome contém caracteres inválidos.";

            normalizedRelativePath = CombineRelative(parentRelPath, name);
            return null;
        }

        /// <summary>
        /// Valida o nome de um ARQUIVO novo (sem barras — só o nome do .json)
        /// a ser criado dentro de <paramref name="parentRelPath"/>. Adiciona
        /// ".json" automaticamente se ausente.
        /// </summary>
        public static string? ValidateFileName(string? rawInput, string parentRelPath, out string normalizedRelativePath)
        {
            normalizedRelativePath = "";
            var name = (rawInput ?? "").Trim().Replace('\\', '/');

            if (string.IsNullOrEmpty(name))
                return "Informe um nome de arquivo.";
            if (name.Contains("..") || Path.IsPathRooted(name) || name.Contains("/"))
                return "Nome inválido — use só o nome do arquivo, sem barras nem \"..\".";
            if (name.IndexOfAny(InvalidNameChars) >= 0)
                return "Nome contém caracteres inválidos.";

            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name += ".json";

            normalizedRelativePath = CombineRelative(parentRelPath, name);
            return null;
        }

        private static string CombineRelative(string parentRelPath, string name)
        {
            parentRelPath = (parentRelPath ?? "").Trim('/');
            return parentRelPath.Length == 0 ? name : $"{parentRelPath}/{name}";
        }
    }
}
