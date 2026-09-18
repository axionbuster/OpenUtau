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
            var match = System.Text.RegularExpressions.Regex.Match(name.Trim().Replace("𝄪", "##").Replace("𝄫", "bb").Replace("x", "##"), @"^([A-Ga-g])([#♯b♭]*)(-?\d+)$");
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
            string accidentalName = accidental < 0
                ? new string('b', -accidental).Replace("bb", "𝄫").Replace("b", "♭")
                : new string('#', accidental).Replace("##", "𝄪").Replace("#", "♯");
            return Letters[letter] + accidentalName;
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
        // Nearest 31-TET step to the 7:4 harmonic interval (about 968.8 cents).
        public const int HarmonicSeventh = 25;
        public static bool IsMajorDegree(int relativeStep) => relativeStep is 0 or 5 or 10 or 13 or 18 or 23 or 28;
        public static bool IsMinorDegree(int relativeStep) => relativeStep is 0 or 5 or 8 or 13 or 18 or 21 or 26;
        public static bool IsDegreeInSelectedScales(int relativeStep, bool major, bool minor) =>
            (major && IsMajorDegree(relativeStep)) || (minor && IsMinorDegree(relativeStep));
        static readonly int[] ScaleSteps = { 0, 5, 8, 10, 13, 18, 21, 23, HarmonicSeventh, 26, 28 };
        static readonly string[] ScaleLabels = { "1", "2", "♭3", "3", "4", "5", "♭6", "6", "H7", "♭7", "7" };
        // Familiar chromatic degrees that can be useful when an out-of-scale note reveals their row.
        // They are not part of either folded scale collection and must not make rows visible by themselves.
        static readonly int[] CommonChromaticSteps = { 2, 3, 7, 11, 12, 15, 16, 20, 24, 30 };
        static readonly string[] CommonChromaticLabels = { "♯1", "♭2", "♯2", "♭4", "♯3", "♯4", "♭5", "♯5", "𝄫7", "♯7" };
        public static bool IsCommonChromaticDegree(int relativeStep) =>
            Array.IndexOf(CommonChromaticSteps, Mod(relativeStep, Divisions)) >= 0;
        public static int ScaleColorIndex(int step, int tonicFifths) =>
            Mod(step - tonicFifths * 18, 31);
        public static string ScaleDegreeLabel(int step, int tonicFifths, bool includeCommonChromatic = false) {
            int relativeStep = ScaleColorIndex(step, tonicFifths);
            int index = Array.IndexOf(ScaleSteps, relativeStep);
            if (index >= 0) { return ScaleLabels[index]; }
            if (includeCommonChromatic) {
                index = Array.IndexOf(CommonChromaticSteps, relativeStep);
                if (index >= 0) { return CommonChromaticLabels[index]; }
            }
            return $"+{relativeStep}";
        }
    }
}
