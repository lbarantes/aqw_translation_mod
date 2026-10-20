using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    public sealed class ManifestFile
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    public sealed class SyncLimits
    {
        public int  MaxFiles         { get; set; } = 5000;
        public long MaxFileBytes     { get; set; } = 8L * 1024 * 1024;
        public long MaxPackageBytes  { get; set; } = 32L * 1024 * 1024;   // ZIP baixado
        public long MaxExtractedBytes{ get; set; } = 128L * 1024 * 1024;  // soma dos arquivos
        public long MaxManifestBytes { get; set; } = 1L * 1024 * 1024;
    }

    // Manifesto publicado junto com cada release (manifest.json). Entrada NÃO
    // confiável: tudo é validado aqui antes de qualquer download.
    //
    //   {
    //     "schemaVersion": 1,
    //     "packageVersion": "2026.09.30",
    //     "minModVersion": "2.2.0",
    //     "package": { "name": "translations.zip", "sha256": "...", "sizeBytes": 12345 },
    //     "files": [ { "path": "ui/hud.json", "sha256": "...", "sizeBytes": 3334 } ]
    //   }
    public sealed class RemoteManifest
    {
        public const int SupportedSchemaVersion = 1;

        private static readonly Regex Sha256Re = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
        private static readonly Regex PackageNameRe = new Regex(@"^[A-Za-z0-9_.-]+\.zip$", RegexOptions.Compiled);

        public int SchemaVersion { get; private set; }
        public PackageVersion PackageVersion { get; private set; } = null!;
        public PackageVersion? MinModVersion { get; private set; }
        public string PackageName { get; private set; } = "";
        public string PackageSha256 { get; private set; } = "";
        public long PackageSizeBytes { get; private set; }
        public IReadOnlyList<ManifestFile> Files { get; private set; } = new List<ManifestFile>();
        /// <summary>Atualização do próprio mod (seção opcional "mod"); null se ausente ou inválida.</summary>
        public ModRelease? Mod { get; private set; }
        /// <summary>Por que a seção "mod" foi ignorada (só quando ela existe e é inválida).</summary>
        public string? ModError { get; private set; }

        public static bool TryParse(string json, SyncLimits limits, out RemoteManifest manifest, out string error)
        {
            manifest = null!;
            try
            {
                var root = JObject.Parse(json);

                var schema = root["schemaVersion"];
                if (schema == null || schema.Type != JTokenType.Integer || (int)schema != SupportedSchemaVersion)
                    return Fail("schemaVersion não suportado", out error);

                if (!PackageVersion.TryParse((string?)root["packageVersion"], out var pv))
                    return Fail("packageVersion inválido", out error);

                PackageVersion? minMod = null;
                var minStr = (string?)root["minModVersion"];
                if (minStr != null && !PackageVersion.TryParse(minStr, out minMod!))
                    return Fail("minModVersion inválido", out error);

                var pkg = root["package"] as JObject;
                var name = (string?)pkg?["name"] ?? "";
                var pkgSha = (string?)pkg?["sha256"] ?? "";
                var pkgSize = (long?)pkg?["sizeBytes"] ?? -1;
                if (!PackageNameRe.IsMatch(name)) return Fail("nome do pacote inválido", out error);
                if (!Sha256Re.IsMatch(pkgSha)) return Fail("sha256 do pacote inválido", out error);
                if (pkgSize <= 0 || pkgSize > limits.MaxPackageBytes) return Fail("tamanho do pacote fora do limite", out error);

                var filesTok = root["files"] as JArray;
                if (filesTok == null) return Fail("lista de arquivos ausente", out error);
                if (filesTok.Count > limits.MaxFiles) return Fail("arquivos demais no pacote", out error);

                var files = new List<ManifestFile>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var f in filesTok)
                {
                    var path = (string?)f["path"] ?? "";
                    var sha  = (string?)f["sha256"] ?? "";
                    var size = (long?)f["sizeBytes"] ?? -1;

                    var why = SyncPaths.RejectReason(path);
                    if (why != null) return Fail($"'{path}': {why}", out error);
                    if (!Sha256Re.IsMatch(sha)) return Fail($"'{path}': sha256 inválido", out error);
                    if (size < 0 || size > limits.MaxFileBytes) return Fail($"'{path}': tamanho fora do limite", out error);
                    if (!seen.Add(path)) return Fail($"'{path}': duplicado", out error);

                    total += size;
                    if (total > limits.MaxExtractedBytes) return Fail("tamanho total fora do limite", out error);
                    files.Add(new ManifestFile { Path = path, Sha256 = sha.ToLowerInvariant(), SizeBytes = size });
                }

                // seção "mod" inválida não derruba as traduções: só não atualiza o mod
                ModRelease? mod = null;
                string? modError = null;
                if (root["mod"] is JObject modNode && !ModRelease.TryParse(modNode, out mod, out var whyMod))
                {
                    mod = null;
                    modError = whyMod;
                }

                manifest = new RemoteManifest
                {
                    Mod              = mod,
                    ModError         = modError,
                    SchemaVersion    = SupportedSchemaVersion,
                    PackageVersion   = pv,
                    MinModVersion    = minMod,
                    PackageName      = name,
                    PackageSha256    = pkgSha.ToLowerInvariant(),
                    PackageSizeBytes = pkgSize,
                    Files            = files,
                };
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                return Fail("manifesto ilegível: " + ex.Message, out error);
            }
        }

        private static bool Fail(string msg, out string error) { error = msg; return false; }
    }
}
