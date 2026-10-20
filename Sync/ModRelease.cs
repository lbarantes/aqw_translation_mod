using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    public sealed class ModEditionAsset
    {
        public string Name { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    // Seção opcional "mod" do manifest.json: a DLL de cada edição e a
    // assinatura RSA de tudo isso, feita com a chave privada que só existe no
    // PC de quem publica (tools/new-signing-key.ps1). Cliente antigo ignora a
    // seção; manifesto sem ela só atualiza traduções.
    //
    //   "mod": {
    //     "version": "0.2.0",
    //     "editions": { "Player": { "name": "AQWTranslation-Player.dll", "sha256": "...", "sizeBytes": 99328 },
    //                   "Translator": { ... } },
    //     "signature": "<base64>"
    //   }
    public sealed class ModRelease
    {
        public const long MaxDllBytes = 16L * 1024 * 1024;

        private static readonly Regex Sha256Re   = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);
        private static readonly Regex EditionRe  = new Regex("^[A-Za-z]{1,32}$", RegexOptions.Compiled);
        private static readonly Regex AssetNameRe = new Regex(@"^AQWTranslation-[A-Za-z]{1,32}\.dll$", RegexOptions.Compiled);

        public PackageVersion Version { get; private set; } = null!;
        public IReadOnlyDictionary<string, ModEditionAsset> Editions { get; private set; } =
            new Dictionary<string, ModEditionAsset>();
        public byte[] Signature { get; private set; } = new byte[0];

        public static bool TryParse(JObject node, out ModRelease release, out string error)
        {
            release = null!;
            try
            {
                if (!PackageVersion.TryParse((string?)node["version"], out var version))
                    return Fail("versão do mod inválida", out error);

                var eds = node["editions"] as JObject;
                if (eds == null || eds.Count == 0) return Fail("sem edições", out error);

                var editions = new Dictionary<string, ModEditionAsset>(StringComparer.Ordinal);
                foreach (var p in eds.Properties())
                {
                    var name = (string?)p.Value["name"] ?? "";
                    var sha  = ((string?)p.Value["sha256"] ?? "").ToLowerInvariant();
                    var size = (long?)p.Value["sizeBytes"] ?? -1;
                    if (!EditionRe.IsMatch(p.Name)) return Fail($"edição '{p.Name}' inválida", out error);
                    if (!AssetNameRe.IsMatch(name)) return Fail($"'{p.Name}': nome de arquivo inválido", out error);
                    if (!Sha256Re.IsMatch(sha)) return Fail($"'{p.Name}': sha256 inválido", out error);
                    if (size <= 0 || size > MaxDllBytes) return Fail($"'{p.Name}': tamanho fora do limite", out error);
                    editions[p.Name] = new ModEditionAsset { Name = name, Sha256 = sha, SizeBytes = size };
                }

                byte[] sig;
                try { sig = Convert.FromBase64String((string?)node["signature"] ?? ""); }
                catch (FormatException) { return Fail("assinatura ilegível", out error); }
                if (sig.Length == 0) return Fail("sem assinatura", out error);

                release = new ModRelease { Version = version, Editions = editions, Signature = sig };
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                return Fail("seção 'mod' ilegível: " + ex.Message, out error);
            }
        }

        /// <summary>
        /// Texto exato que a assinatura cobre. tools/build-release.ps1 monta o
        /// mesmo texto — qualquer mudança aqui precisa ir pra lá também.
        /// </summary>
        public static string SignedPayload(string version, IEnumerable<KeyValuePair<string, ModEditionAsset>> editions)
        {
            var sb = new StringBuilder("aqwtranslation-mod-release-v1\nversion=").Append(version).Append('\n');
            foreach (var e in editions.OrderBy(e => e.Key, StringComparer.Ordinal))
                sb.Append(e.Key).Append('=').Append(e.Value.Name).Append(':')
                  .Append(e.Value.Sha256).Append(':').Append(e.Value.SizeBytes).Append('\n');
            return sb.ToString();
        }

        /// <summary>RSA PKCS#1 v1.5 + SHA-256 sobre SignedPayload.</summary>
        public bool VerifySignature(string publicKeyXml)
        {
            try
            {
                var data = new UTF8Encoding(false).GetBytes(SignedPayload(Version.Raw, Editions));
                using var rsa = new RSACryptoServiceProvider();
                rsa.FromXmlString(publicKeyXml);
                return rsa.VerifyData(data, "SHA256", Signature);
            }
            catch
            {
                return false;
            }
        }

        private static bool Fail(string msg, out string error) { error = msg; return false; }
    }

    internal static class ModSignature
    {
        // par de tools/new-signing-key.ps1; a privada fica fora do repositório
        public const string PublicKeyXml =
            "<RSAKeyValue><Modulus>1/WePxs0PZb9X5YQYycrHPVP4hz3bVsMO0akthm9MQcVUc+0FcPG4MtNcC2WMVRjRxqIDkLZ6ks0tapEYAo2Gmfa5N9R7nDkJgOkYx91hiFC9OivV7qhOx4U/MbBQBvvlRNLCfdWXuMuYNXobObawS85AhP1QKkpaGaasTHQJ382Owp8clADsAVyXZUYvC5g8Hu0RiCDg843kcvhR7iaSAZa3+dJZpzI4tPdkJhCYEfIG8jZWN/zZoz3zE2SHtgiSIhFX5CMJ2HFpxhIzfQcr7a3UGFMj2HWt2pGFaNIuTXD0KbI3zHEubBL+L6gQGYIR53irltL1J7CRUzzJKWxC9ZXtb+kFANQEk5TG75QSnNcjQlXefdOT73dCAjoIrRXApS6sywh6EfrGmP4wXDbLLw9Xwb8KLsQNIaC88Pa4JDW2yKbGt3nYClIEfqr5HlDejTEKU881gx4dzzWnVF+cbsmep5m0GFlXVHCWHrj8p7K4beNVB4DvvbUFQW3kjtd</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}
