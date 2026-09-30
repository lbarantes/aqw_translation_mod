using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AQWMod.Localization.Utils
{
    public static class RichTextUtils
    {
        private static readonly Regex TagRegex = new Regex(
            @"<\/?(?:b|i|u|s|nobr|br|color|size|alpha|cspace|mspace|voffset|" +
            @"font|sprite|link|mark|rotate|uppercase|lowercase|smallcaps|gradient|style)" +
            @"(?:=[^>]*)?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static bool HasRichText(string text) =>
            text.IndexOf('<') >= 0 && TagRegex.IsMatch(text);

        public static string StripAndCapture(string text, out List<string> tags)
        {
            var capturedTags = new List<string>();
            tags = capturedTags;
            if (!HasRichText(text)) return text;
            int tagIndex = 0;
            return TagRegex.Replace(text, match =>
            {
                capturedTags.Add(match.Value);
                return "\x01" + tagIndex++ + "\x01";
            });
        }

        public static string Reinject(string translatedText, List<string> tags)
        {
            if (tags == null || tags.Count == 0) return translatedText;
            return Regex.Replace(translatedText, @"\x01(\d+)\x01", match =>
            {
                int idx = int.Parse(match.Groups[1].Value);
                return idx < tags.Count ? tags[idx] : string.Empty;
            });
        }

        public static string StripAll(string text) =>
            HasRichText(text) ? TagRegex.Replace(text, string.Empty) : text;
    }
}
