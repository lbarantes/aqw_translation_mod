using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AQWMod.Localization.GitHubPr
{
    public interface ISecretProtector
    {
        byte[] Protect(byte[] plain);
        byte[] Unprotect(byte[] protectedData);
    }

    // DPAPI do Windows no escopo do usuário atual: o arquivo só é legível
    // pela mesma conta do Windows, nesta máquina.
    public sealed class DpapiSecretProtector : ISecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AQWTranslation.github.v1");

        public byte[] Protect(byte[] plain) =>
            ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

        public byte[] Unprotect(byte[] protectedData) =>
            ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
    }

    // Guarda o token de acesso do GitHub cifrado, fora das pastas de tradução
    // e fora do repositório (BepInEx/config). Se não for possível cifrar, NÃO
    // grava em texto puro — o usuário só fica conectado até fechar o jogo.
    public sealed class TokenStore
    {
        private readonly string _path;
        private readonly ISecretProtector _protector;

        public TokenStore(string path, ISecretProtector protector)
        {
            _path = path;
            _protector = protector;
        }

        public string? Load()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var raw = Convert.FromBase64String(File.ReadAllText(_path, Encoding.ASCII).Trim());
                var plain = _protector.Unprotect(raw);
                var token = Encoding.UTF8.GetString(plain);
                return token.Length > 0 ? token : null;
            }
            catch
            {
                // arquivo de outra conta/máquina ou corrompido: trata como "sem login"
                return null;
            }
        }

        /// <summary>true se gravou; false se não deu pra cifrar (o token não é gravado).</summary>
        public bool Save(string token)
        {
            try
            {
                var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(token));
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, Convert.ToBase64String(protectedBytes), Encoding.ASCII);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Clear()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }
    }
}
