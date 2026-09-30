using System;
using System.Linq;

namespace AQWMod.Localization.Sync
{
    // Versão numérica pontuada ("2026.09.30", "2026.09.30.2", "2.2.0"). Compara
    // campo a campo como número; campo ausente vale 0. Não é semver completo —
    // só precisa de uma ordem monotônica e sem ambiguidade.
    public sealed class PackageVersion : IComparable<PackageVersion>
    {
        private readonly int[] _parts;
        public string Raw { get; }

        private PackageVersion(string raw, int[] parts) { Raw = raw; _parts = parts; }

        public static bool TryParse(string? s, out PackageVersion version)
        {
            version = null!;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var segs = s!.Trim().Split('.');
            if (segs.Length < 1 || segs.Length > 4) return false;

            var parts = new int[segs.Length];
            for (int i = 0; i < segs.Length; i++)
            {
                if (segs[i].Length == 0 || segs[i].Length > 9 || !segs[i].All(char.IsDigit)) return false;
                parts[i] = int.Parse(segs[i]);
            }
            version = new PackageVersion(s.Trim(), parts);
            return true;
        }

        public int CompareTo(PackageVersion? other)
        {
            if (other == null) return 1;
            var n = Math.Max(_parts.Length, other._parts.Length);
            for (int i = 0; i < n; i++)
            {
                var a = i < _parts.Length ? _parts[i] : 0;
                var b = i < other._parts.Length ? other._parts[i] : 0;
                if (a != b) return a.CompareTo(b);
            }
            return 0;
        }

        public override string ToString() => Raw;
    }
}
