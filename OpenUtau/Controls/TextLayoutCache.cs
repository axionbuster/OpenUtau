using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace OpenUtau.App.Controls {
    static class TextLayoutCache {
        private static readonly Dictionary<Tuple<string, IBrush, double, bool, bool, double>, TextLayout> cache
            = new Dictionary<Tuple<string, IBrush, double, bool, bool, double>, TextLayout>();

        public static void Clear() {
            cache.Clear();
        }

        public static double CompactAccidentalLetterSpacing(string text) =>
            text.Contains("♭♭", StringComparison.Ordinal) || text.Contains("♯♯", StringComparison.Ordinal)
                ? -0.4
                : 0;

        public static TextLayout Get(string text, IBrush brush, double fontSize, bool bold = false,
                bool italic = false, double letterSpacing = 0) {
            var key = Tuple.Create(text, brush, fontSize, bold, italic, letterSpacing);
            if (!cache.TryGetValue(key, out var textLayout)) {
                var fontWeight = bold ? FontWeight.Bold : FontWeight.Normal;
                textLayout = new TextLayout(
                    text,
                    new Typeface(FontFamily.Default, italic ? FontStyle.Italic : FontStyle.Normal, fontWeight),
                    fontSize,
                    brush,
                    TextAlignment.Left,
                    TextWrapping.NoWrap,
                    letterSpacing: letterSpacing);
                cache.Add(key, textLayout);
            }
            return textLayout;
        }
    }
}
