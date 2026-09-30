using System.Collections.Generic;
using AQWMod.Localization.Core;
using AQWMod.Localization.Utils;

namespace AQWMod.Localization.Pipeline.Stages
{
    // Preserva tags TMP rich text durante a tradução.
    //
    // strip=true (antes do lookup): "Attack <color=red>Mobius</color>!" vira
    // "Attack \x010\x01Mobius\x011\x01!", tags salvas em ctx.Metadata["_rich_tags"].
    // strip=false (depois do lookup, só se IsTranslated): reverte, reinjetando
    // as tags de volta no texto traduzido. Sem rich text, os dois modos são no-op.
    public sealed class RichTextStage : IPipelineStage
    {
        private const string MetaKey = "_rich_tags";
        private readonly bool _strip;

        public RichTextStage(bool strip) => _strip = strip;

        public bool Process(PipelineContext ctx)
        {
            if (_strip)
            {
                if (!RichTextUtils.HasRichText(ctx.CurrentText))
                {
                    ctx.HasRichText = false;
                    return true;
                }

                var stripped = RichTextUtils.StripAndCapture(ctx.CurrentText, out var tags);
                ctx.HasRichText   = true;
                ctx.CurrentText   = stripped;
                ctx.Metadata[MetaKey] = SerializeTags(tags);
            }
            else
            {
                // só reinjeta se havia rich text E a tradução foi feita
                if (!ctx.HasRichText || !ctx.IsTranslated) return true;

                if (!ctx.Metadata.TryGetValue(MetaKey, out var tagsJson)) return true;

                var tags = DeserializeTags(tagsJson);
                ctx.CurrentText = RichTextUtils.Reinject(ctx.CurrentText, tags);
            }

            return true;
        }

        // serialização manual, sem JSON completo, pra não pesar no hotpath
        private static string SerializeTags(List<string> tags) =>
            string.Join("\x02", tags); // \x02 nunca aparece em rich text TMP de verdade

        private static List<string> DeserializeTags(string serialized)
        {
            return new List<string>(serialized.Split('\x02'));
        }
    }
}
