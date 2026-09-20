using System;
using System.Linq;
using Avalonia.Media;
using OpenUtau.Core.Util;

namespace OpenUtau.App {
    /// <summary>Continuous root-relative color circle shared by keyboard rows and harmonic controls.</summary>
    static class DegreeColorPalette {
        static readonly string[] BaseHex = {
            "#A0C8F4", "#A9C5F6", "#B4C1F6", "#BEBEF4", "#C8BBF0", "#D2B8EA",
            "#DAB5E3", "#E1B3DB", "#E7B1D1", "#ECB0C7", "#EFB0BC", "#F1B1B2",
            "#F1B2A8", "#EFB49F", "#ECB797", "#E7BA91", "#E1BE8D", "#D9C28C",
            "#D0C68D", "#C6C991", "#BBCC96", "#B1CF9E", "#A6D2A7", "#9CD3B1",
            "#93D4BB", "#8DD5C6", "#89D4D0", "#88D3DA", "#8AD1E3", "#8FCFEA",
            "#97CCF0",
        };

        public static readonly Color[] BaseColors = BaseHex.Select(Color.Parse).ToArray();
        public static readonly IBrush[] Brushes = BaseColors
            .Select(color => (IBrush)new SolidColorBrush(color))
            .ToArray();
        static readonly Color[] ActiveColors = BaseColors.Select(ActiveColor).ToArray();

        public static int IndexForInterval(int step, bool is31Edo) {
            if (is31Edo) {
                return Edo31.Mod(step, Edo31.Divisions);
            }
            return Edo31.Mod((int)Math.Round(
                Edo31.Mod(step, 12) * Edo31.Divisions / 12.0,
                MidpointRounding.AwayFromZero), Edo31.Divisions);
        }

        public static Color Background(int index, bool active) =>
            (active ? ActiveColors : BaseColors)[Edo31.Mod(index, Edo31.Divisions)];

        public static Color Foreground(Color background) =>
            Contrast(background, Avalonia.Media.Colors.Black) >= Contrast(background, Avalonia.Media.Colors.White)
                ? Avalonia.Media.Colors.Black
                : Avalonia.Media.Colors.White;

        public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        static Color ActiveColor(Color color) {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double hue;
            if (delta == 0) {
                hue = 0;
            } else if (max == r) {
                hue = 60 * ((g - b) / delta % 6);
                if (hue < 0) {
                    hue += 360;
                }
            } else if (max == g) {
                hue = 60 * ((b - r) / delta + 2);
            } else {
                hue = 60 * ((r - g) / delta + 4);
            }
            double lightness = (max + min) / 2;
            double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
            saturation = Math.Max(0.7, saturation);
            const double preferredLightness = 0.4;
            var active = FromHsl(hue, saturation, preferredLightness);
            if (Contrast(active, Avalonia.Media.Colors.White) >= 4.5) {
                return active;
            }
            double low = 0.05;
            double high = preferredLightness;
            for (int iteration = 0; iteration < 12; iteration++) {
                double middle = (low + high) / 2;
                var candidate = FromHsl(hue, saturation, middle);
                if (Contrast(candidate, Avalonia.Media.Colors.White) >= 4.5) {
                    low = middle;
                } else {
                    high = middle;
                }
            }
            return FromHsl(hue, saturation, low);
        }

        static Color FromHsl(double hue, double saturation, double lightness) {
            double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
            (double r, double g, double b) = hue switch {
                < 60 => (chroma, x, 0d),
                < 120 => (x, chroma, 0d),
                < 180 => (0d, chroma, x),
                < 240 => (0d, x, chroma),
                < 300 => (x, 0d, chroma),
                _ => (chroma, 0d, x),
            };
            double m = lightness - chroma / 2;
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        static double Contrast(Color first, Color second) {
            double firstLuminance = Luminance(first);
            double secondLuminance = Luminance(second);
            double lighter = Math.Max(firstLuminance, secondLuminance);
            double darker = Math.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05) / (darker + 0.05);
        }

        static double Luminance(Color color) =>
            0.2126 * Linear(color.R / 255.0) +
            0.7152 * Linear(color.G / 255.0) +
            0.0722 * Linear(color.B / 255.0);

        static double Linear(double component) => component <= 0.04045
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);
    }
}
