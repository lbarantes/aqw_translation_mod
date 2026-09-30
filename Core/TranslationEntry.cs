namespace AQWMod.Localization.Core
{
    // Valor associado a uma TranslationKey no repositório. Só o Text entra
    // no hotpath de tradução — o resto é metadado, usado pela GUI e afins.
    public sealed class TranslationEntry
    {
        /// <summary>Texto final aplicado pelo pipeline (entrada "ignorada" já vem igual à chave).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>draft | translated | reviewed | outdated | ignored | null.</summary>
        public string? Status { get; set; }

        /// <summary>manual | auto | imported | null (sem info = trata como manual).</summary>
        public string? Source { get; set; }

        public string[]? Tags { get; set; }
    }
}
