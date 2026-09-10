using System;

namespace OpenUtau.Core.Util {
    /// <summary>Pitch arithmetic and display-only chain-of-fifths spelling.</summary>
    public static class Edo31 {
        public const int Divisions = 31;
        public const double StepTone = 12.0 / Divisions;
        public const int MaxStep = 341; // Eleven octaves, matching the ordinary editor.
        static readonly string[] Letters = { "F", "C", "G", "D", "A", "E", "B" };
        static readonly int[] Naturals = { 13, 0, 18, 5, 23, 10, 28 };
        public static int Mod(int n, int d) => (n % d + d) % d;
        public static int NearestStep(double tone) => (int)Math.Round(tone / StepTone, MidpointRounding.AwayFromZero);
        public static int ParseName(string name) {
            var match = System.Text.RegularExpressions.Regex.Match(name.Trim(), @"^([A-Ga-g])([#♯b♭]*)(-?\d+)$");
            if (!match.Success) { return -1; }
            int letter = Array.IndexOf(Letters, match.Groups[1].Value.ToUpperInvariant());
            int accidental = 0;
            foreach (char c in match.Groups[2].Value) { accidental += c == '#' || c == '♯' ? 1 : -1; }
            if (!int.TryParse(match.Groups[3].Value, out int octave) || octave < -2 || octave > 11) { return -1; }
            int step = (octave + 1) * 31 + Naturals[letter] + accidental * 2;
            return step >= 0 && step < MaxStep ? step : -1;
        }
        public static string FifthName(int fifths) {
            int letter = Mod(fifths + 1, 7);
            int accidental = (fifths + 1 - letter) / 7;
            return Letters[letter] + (accidental < 0 ? new string('♭', -accidental) : new string('♯', accidental));
        }
        public static int FifthsForStep(int step, int preferredFifths) {
            int lower = preferredFifths - 15;
            // 19 is the modular inverse of the 18-step fifth.
            return lower + Mod(Mod(step, 31) * 19 - lower, 31);
        }
        public static string Name(int step, int preferredFifths) {
            int fifths = FifthsForStep(step, preferredFifths);
            int letter = Mod(fifths + 1, 7);
            int accidental = (fifths + 1 - letter) / 7;
            int octave = (step - Naturals[letter] - accidental * 2) / 31 - 1;
            return FifthName(fifths) + octave;
        }
        // Fixed D-centered spelling supplies five color families. Respelling never changes them.
        public static int ColorFamily(int step) {
            int fifths = FifthsForStep(step, 2);
            int letter = Mod(fifths + 1, 7);
            return (fifths + 1 - letter) / 7 + 2;
        }
    }
}
