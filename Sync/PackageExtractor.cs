using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace AQWMod.Localization.Sync
{
    public sealed class PackageValidationException : Exception
    {
        public PackageValidationException(string message) : base(message) { }
    }

    // Confere e extrai o ZIP baixado. O conteúdo do ZIP só vale se bater EXATAMENTE
    // com o manifesto: mesmos arquivos, mesmos tamanhos, mesmos sha256. Qualquer
    // diferença (arquivo a mais, a menos, adulterado, caminho estranho) aborta a
    // atualização inteira — nada vai pra pasta de destino parcialmente.
    public static class PackageExtractor
    {
        public static string Sha256OfFile(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(fs));
        }

        /// <summary>Verifica tamanho e hash do próprio ZIP contra o manifesto.</summary>
        public static void VerifyPackageFile(string zipPath, RemoteManifest manifest)
        {
            var size = new FileInfo(zipPath).Length;
            if (size != manifest.PackageSizeBytes)
                throw new PackageValidationException($"tamanho do pacote ({size}) difere do manifesto ({manifest.PackageSizeBytes})");
            if (!string.Equals(Sha256OfFile(zipPath), manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new PackageValidationException("sha256 do pacote difere do manifesto");
        }

        public static void ExtractVerified(string zipPath, RemoteManifest manifest, string destDir, SyncLimits limits)
        {
            var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destRoot = Path.GetFullPath(destDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            long total = 0;

            Directory.CreateDirectory(destDir);
            using var zip = ZipFile.OpenRead(zipPath);

            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.EndsWith("/", StringComparison.Ordinal)) continue; // entrada de pasta

                var why = SyncPaths.RejectReason(name);
                if (why != null) throw new PackageValidationException($"entrada '{name}': {why}");
                if (!expected.TryGetValue(name, out var mf))
                    throw new PackageValidationException($"entrada '{name}' não está no manifesto");
                if (!seen.Add(name))
                    throw new PackageValidationException($"entrada '{name}' duplicada");
                if (entry.Length != mf.SizeBytes)
                    throw new PackageValidationException($"'{name}': tamanho difere do manifesto");

                total += entry.Length;
                if (total > limits.MaxExtractedBytes)
                    throw new PackageValidationException("tamanho total extraído acima do limite");

                var dest = Path.GetFullPath(Path.Combine(destRoot, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                    throw new PackageValidationException($"'{name}' escaparia da pasta de destino");

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var hash = CopyAndHash(entry, dest, mf.SizeBytes);
                if (!string.Equals(hash, mf.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new PackageValidationException($"'{name}': sha256 difere do manifesto");
            }

            var missing = expected.Keys.Where(k => !seen.Contains(k)).ToList();
            if (missing.Count > 0)
                throw new PackageValidationException($"pacote incompleto: faltam {missing.Count} arquivo(s), ex.: '{missing[0]}'");
        }

        // não confia no tamanho declarado pelo ZIP: corta o fluxo um byte acima do
        // esperado, pra um ZIP mentiroso não encher o disco
        private static string CopyAndHash(ZipArchiveEntry entry, string dest, long expectedSize)
        {
            using var input = entry.Open();
            using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            using var sha = SHA256.Create();

            var buffer = new byte[81920];
            long written = 0;
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += n;
                if (written > expectedSize)
                    throw new PackageValidationException($"'{entry.FullName}': conteúdo maior que o declarado");
                sha.TransformBlock(buffer, 0, n, null, 0);
                output.Write(buffer, 0, n);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return Hex(sha.Hash!);
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
