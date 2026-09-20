using System;

namespace OpenUtau.Core.Util {
    public enum Edo31PitchReferenceMode {
        A4Frequency,
        TwelveTetNote,
    }

    /// <summary>
    /// Project-level absolute pitch reference for native 31-TET documents.
    /// The editor grid remains step-relative; this reference shifts sounding pitch only.
    /// </summary>
    public sealed class Edo31PitchReference {
        public Edo31PitchReferenceMode Mode { get; set; } = Edo31PitchReferenceMode.A4Frequency;
        public double A4Frequency { get; set; } = 440;
        public int TwelveTetPitchClass { get; set; } = 9;

        public static Edo31PitchReference Default => new Edo31PitchReference();
        public static Edo31PitchReference LegacyC => new Edo31PitchReference {
            Mode = Edo31PitchReferenceMode.TwelveTetNote,
            TwelveTetPitchClass = 0,
        };

        private (double Tone, double Frequency) Anchor => Mode switch {
            Edo31PitchReferenceMode.A4Frequency =>
                (Edo31.A4Step * Edo31.StepTone, A4Frequency),
            Edo31PitchReferenceMode.TwelveTetNote => (
                60 + Edo31.NearestStepForTwelveTetPitchClass(TwelveTetPitchClass) * Edo31.StepTone,
                MusicMath.ToneToFreq(60 + TwelveTetPitchClass)),
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };

        public double ToneToFrequency(double tone) {
            var anchor = Anchor;
            return anchor.Frequency * Math.Pow(2, (tone - anchor.Tone) / 12);
        }

        public double FrequencyToTone(double frequency) {
            if (!double.IsFinite(frequency) || frequency <= 0) {
                throw new ArgumentOutOfRangeException(nameof(frequency));
            }
            var anchor = Anchor;
            return anchor.Tone + 12 * Math.Log2(frequency / anchor.Frequency);
        }

        public double EffectiveA4Frequency => ToneToFrequency(Edo31.A4Step * Edo31.StepTone);

        public bool HasSameTuning(Edo31PitchReference other) =>
            Math.Abs(1200 * Math.Log2(EffectiveA4Frequency / other.EffectiveA4Frequency)) < 1e-9;

        public Edo31PitchReference ValidatedCopy() {
            if (Mode == Edo31PitchReferenceMode.A4Frequency &&
                (!double.IsFinite(A4Frequency) || A4Frequency <= 0 || A4Frequency > 20000)) {
                throw new ArgumentOutOfRangeException(nameof(A4Frequency));
            }
            if (Mode == Edo31PitchReferenceMode.TwelveTetNote &&
                (TwelveTetPitchClass < 0 || TwelveTetPitchClass >= 12)) {
                throw new ArgumentOutOfRangeException(nameof(TwelveTetPitchClass));
            }
            return new Edo31PitchReference {
                Mode = Mode,
                A4Frequency = A4Frequency,
                TwelveTetPitchClass = TwelveTetPitchClass,
            };
        }
    }

    /// <summary>Pitch arithmetic and display-only chain-of-fifths spelling.</summary>
    public static class Edo31 {
        public const int Divisions = 31;
        public const double StepTone = 12.0 / Divisions;
        public const int MaxStep = 341; // Eleven octaves, matching the ordinary editor.
        public const int A4Step = 178;
        static readonly string[] Letters = { "F", "C", "G", "D", "A", "E", "B" };
        static readonly int[] Naturals = { 13, 0, 18, 5, 23, 10, 28 };
        public static int Mod(int n, int d) => (n % d + d) % d;
        public static int NearestStep(double tone) => (int)Math.Round(tone / StepTone, MidpointRounding.AwayFromZero);
        public static int NearestStepForTwelveTetPitchClass(int pitchClass) {
            if (pitchClass < 0 || pitchClass >= 12) {
                throw new ArgumentOutOfRangeException(nameof(pitchClass));
            }
            return NearestStep(pitchClass);
        }
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
                ? new string('♭', -accidental)
                : new string('♯', accidental);
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
        // Nearest 31-TET step to 7:4 (about 968.8 cents). In septimal meantone,
        // this pitch is spelled as an augmented sixth rather than a seventh.
        public const int SeptimalAugmentedSixth = 25;
        public static bool IsMajorDegree(int relativeStep) => relativeStep is 0 or 5 or 10 or 13 or 18 or 23 or 28;
        public static bool IsMinorDegree(int relativeStep) => relativeStep is 0 or 5 or 8 or 13 or 18 or 21 or 26;
        public static bool IsDegreeInSelectedScales(int relativeStep, bool major, bool minor) =>
            (major && IsMajorDegree(relativeStep)) || (minor && IsMinorDegree(relativeStep));
        // Fifth-based scale-degree spellings for one octave of septimal meantone.
        // Every entry agrees with the note spelling selected from the 31-note fifth chain.
        static readonly string[] ScaleDegreeLabels = {
            "1", "♭♭2", "♯1", "♭2", "♯♯1", "2", "♭♭3", "♯2",
            "♭3", "♭♭4", "3", "♭4", "♯3", "4", "♭♭5", "♯4",
            "♭5", "♯♯4", "5", "♭♭6", "♯5", "♭6", "♯♯5", "6",
            "♭♭7", "♯6", "♭7", "♭♭1", "7", "♭1", "♯7",
        };
        public static int ScaleColorIndex(int step, int tonicFifths) =>
            Mod(step - tonicFifths * 18, 31);
        public static string ScaleDegreeLabel(int step, int tonicFifths) =>
            ScaleDegreeLabels[ScaleColorIndex(step, tonicFifths)];
    }
}
