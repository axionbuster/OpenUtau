using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace OpenUtau.App.Controls {
    static class TextLayoutCache {
        private static readonly Dictionary<Tuple<string, IBrush, double, bool, bool>, TextLayout> cache
            = new Dictionary<Tuple<string, IBrush, double, bool, bool>, TextLayout>();

        public static void Clear() {
            cache.Clear();
        }

        public static TextLayout Get(string text, IBrush brush, double fontSize, bool bold = false, bool italic = false) {
            var key = Tuple.Create(text, brush, fontSize, bold, italic);
            if (!cache.TryGetValue(key, out var textLayout)) {
                var fontWeight = bold ? FontWeight.Bold : FontWeight.Normal;
                textLayout = new TextLayout(
                    text,
                    new Typeface(FontFamily.Default, italic ? FontStyle.Italic : FontStyle.Normal, fontWeight),
                    fontSize,
                    brush,
                    TextAlignment.Left,
                    TextWrapping.NoWrap);
                cache.Add(key, textLayout);
            }
            return textLayout;
        }
    }
}
