namespace AQWMod.Localization
{
    // Hash rápido pra usar como chave de cache. FNV-1a 64 bits: não-criptográfico,
    // rápido, boa distribuição pra strings curtas/médias.
    public static class HashUtils
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime       = 1099511628211UL;

        public static ulong Fnv1a64(string text)
        {
            var hash = FnvOffsetBasis;
            foreach (char c in text)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= FnvPrime;
                hash ^= (byte)(c >> 8);
                hash *= FnvPrime;
            }
            return hash;
        }
    }
}
