using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AQWMod.Localization.Overlay
{
    // Mover e excluir arquivos/pastas da árvore de translations/. Puro
    // sistema de arquivos, sem Unity — a GUI (TreeActions.cs) só chama daqui
    // e depois recarrega os .json devolvidos.
    //
    // Excluir pasta nunca apaga de verdade: a pasta vai inteira pra
    // translations/_trash/<data_hora>/, que o carregamento, o hot reload, a
    // sincronização e a PR ignoram (segmento começando com "_"). Desfazer é
    // mover de volta à mão.
    internal static class TreeNodeOps
    {
        public const string TrashFolderName = "_trash";

        // arquivos da raiz com papel especial: uncategorized.json é camada de
        // override e não recebe mais tradução nova; npc_names.json é a tabela
        // do placeholder {npc}
        private static readonly string[] ReservedRootFiles = { "uncategorized.json", "npc_names.json" };

        public static bool IsReservedRootFile(string relPath) =>
            !relPath.Contains('/') &&
            ReservedRootFiles.Any(n => string.Equals(n, relPath, StringComparison.OrdinalIgnoreCase));

        public static bool IsUncategorized(string relPath) =>
            string.Equals(relPath.Trim('/'), "uncategorized.json", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Confere se <paramref name="srcRel"/> pode ir para dentro de
        /// <paramref name="destFolderRel"/> ("" = raiz). null = pode, com
        /// <paramref name="destRel"/> preenchido; senão a mensagem de erro.
        /// </summary>
        public static string? ValidateMove(string root, string srcRel, bool isFolder, string destFolderRel, out string destRel)
        {
            srcRel        = Normalize(srcRel);
            destFolderRel = Normalize(destFolderRel);
            var name = srcRel.Split('/').Last();
            destRel  = destFolderRel.Length == 0 ? name : destFolderRel + "/" + name;

            if (srcRel.Length == 0) return "Nada selecionado.";
            if (!isFolder && IsReservedRootFile(srcRel))
                return $"'{srcRel}' é um arquivo especial e não pode ser movido.";
            if (string.Equals(srcRel, destRel, StringComparison.OrdinalIgnoreCase))
                return "Já está nessa pasta.";
            if (isFolder && (string.Equals(destFolderRel, srcRel, StringComparison.OrdinalIgnoreCase) ||
                             destFolderRel.StartsWith(srcRel + "/", StringComparison.OrdinalIgnoreCase)))
                return "Não dá pra mover uma pasta para dentro dela mesma.";
            if (!isFolder && IsReservedRootFile(destRel))
                return $"'{destRel}' é um nome reservado na raiz.";

            var srcFull  = ToFull(root, srcRel);
            var destFull = ToFull(root, destRel);
            if (isFolder ? !Directory.Exists(srcFull) : !File.Exists(srcFull))
                return $"'{srcRel}' não existe mais no disco.";
            if (File.Exists(destFull) || Directory.Exists(destFull))
                return $"Já existe '{destRel}' no destino.";
            return null;
        }

        /// <summary>
        /// Move arquivo ou pasta (já validado por ValidateMove). Devolve os
        /// .json afetados como (caminho antigo, caminho novo), completos.
        /// </summary>
        public static List<(string OldFull, string NewFull)> Move(string root, string srcRel, bool isFolder, string destRel)
        {
            var srcFull  = ToFull(root, srcRel);
            var destFull = ToFull(root, destRel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFull)!);

            if (!isFolder)
            {
                File.Move(srcFull, destFull);
                return new List<(string, string)> { (srcFull, destFull) };
            }

            var files = Directory.GetFiles(srcFull, "*.json", SearchOption.AllDirectories);
            Directory.Move(srcFull, destFull);
            return files.Select(f => (f, destFull + f.Substring(srcFull.Length))).ToList();
        }

        /// <summary>
        /// Leva a pasta para translations/_trash/&lt;carimbo&gt;/&lt;caminho&gt;.
        /// Devolve os .json que saíram (caminhos antigos, completos) e o
        /// caminho relativo do destino na lixeira.
        /// </summary>
        public static (List<string> RemovedFiles, string TrashRel) TrashFolder(string root, string folderRel, DateTime now)
        {
            folderRel = Normalize(folderRel);
            if (folderRel.Length == 0) throw new InvalidOperationException("A raiz não pode ser excluída.");
            if (folderRel.Split('/').Any(s => s.StartsWith("_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Pasta interna do mod.");

            var srcFull = ToFull(root, folderRel);
            if (!Directory.Exists(srcFull)) throw new DirectoryNotFoundException(folderRel);

            var stamp    = now.ToString("yyyyMMdd_HHmmss");
            var trashRel = $"{TrashFolderName}/{stamp}/{folderRel}";
            for (int i = 2; Directory.Exists(ToFull(root, trashRel)); i++)
                trashRel = $"{TrashFolderName}/{stamp}_{i}/{folderRel}";

            var files    = Directory.GetFiles(srcFull, "*.json", SearchOption.AllDirectories).ToList();
            var destFull = ToFull(root, trashRel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFull)!);
            Directory.Move(srcFull, destFull);
            return (files, trashRel);
        }

        /// <summary>Quantos .json e quantas subpastas há dentro da pasta.</summary>
        public static (int Files, int Folders) CountContents(string root, string folderRel)
        {
            var full = ToFull(root, Normalize(folderRel));
            if (!Directory.Exists(full)) return (0, 0);
            return (Directory.GetFiles(full, "*.json", SearchOption.AllDirectories).Length,
                    Directory.GetDirectories(full, "*", SearchOption.AllDirectories).Length);
        }

        private static string Normalize(string rel) => (rel ?? "").Replace('\\', '/').Trim('/');

        private static string ToFull(string root, string rel) =>
            rel.Length == 0 ? root : Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
    }
}
