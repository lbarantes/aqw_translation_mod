using System.Collections.Generic;

namespace AQWMod.Localization.Core
{
    // Dados imutáveis de uma requisição de tradução. Criado no patch Harmony
    // e passado por todo o pipeline.
    public sealed class TranslationContext
    {
        public string Text { get; }
        public string ComponentPath { get; }
        public bool IsFromBackend { get; }

        /// <summary>Contexto opcional de desambiguação (ex.: "skill", "ui.button").</summary>
        public string? Context { get; }

        public TranslationContext(string text, string componentPath = "", bool isFromBackend = false, string? context = null)
        {
            Text = text;
            ComponentPath = componentPath ?? string.Empty;
            IsFromBackend = isFromBackend;
            Context = context;
        }

        public static TranslationContext Simple(string text) =>
            new TranslationContext(text, string.Empty, false);
    }

    // Estado mutável que flui pelos stages do pipeline.
    public sealed class PipelineContext
    {
        public string OriginalText { get; }
        public string CurrentText { get; set; }
        public string ComponentPath { get; }
        public bool IsTranslated { get; set; }
        public bool ShouldCapture { get; set; } = true;
        public bool HasRichText { get; set; }
        public bool IsFromBackend { get; }

        /// <summary>Ver TranslationContext.Context.</summary>
        public string? Context { get; }

        // placeholders extraídos ({quest_name} -> valor real) e tags rich text,
        // guardados aqui pra reinjeção depois
        public Dictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

        public PipelineContext(TranslationContext ctx)
        {
            OriginalText = ctx.Text;
            CurrentText  = ctx.Text;
            ComponentPath = ctx.ComponentPath;
            IsFromBackend = ctx.IsFromBackend;
            Context       = ctx.Context;
        }
    }

    // Resultado imutável retornado pelo pipeline.
    public readonly struct PipelineResult
    {
        public string Text { get; }
        public bool IsTranslated { get; }

        public PipelineResult(string text, bool isTranslated)
        {
            Text = text;
            IsTranslated = isTranslated;
        }
    }
}
