using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    /// <summary>
    /// A root-relative chord member. Degree identity is retained so that a
    /// helper converts between 12-TET and 31-TET without rounding a pitch mask.
    /// </summary>
    public sealed class UChordInterval : IEquatable<UChordInterval> {
        public int degree = 1;
        public int alteration;

        public UChordInterval() { }
        public UChordInterval(int degree, int alteration = 0) {
            this.degree = degree;
            this.alteration = alteration;
        }

        public int Offset(bool is31Edo) => ChordHelperTheory.IntervalOffset(degree, alteration, is31Edo);
        [YamlIgnore] public string Label => ChordHelperTheory.IntervalLabel(degree, alteration);
        public UChordInterval Clone() => new UChordInterval(degree, alteration);

        public bool Equals(UChordInterval? other) =>
            other != null && degree == other.degree && alteration == other.alteration;
        public override bool Equals(object? obj) => Equals(obj as UChordInterval);
        public override int GetHashCode() => HashCode.Combine(degree, alteration);
    }

    /// <summary>
    /// Silent harmonic annotation owned by a voice part. Position is relative
    /// to the part, just like notes and curves; it is never a performed note.
    /// </summary>
    public sealed class UChordHelper {
        public int position;
        public int duration = 480;
        public int root;
        public List<UChordInterval> tones = ChordHelperTheory.CreatePreset("Major");
        public UChordInterval? bass;
        public bool highlightRoot = true;
        public string color = "#35A7D8";

        [YamlIgnore] public int End => position + duration;

        public UChordHelper Clone() => new UChordHelper {
            position = position,
            duration = duration,
            root = root,
            tones = tones.Select(tone => tone.Clone()).ToList(),
            bass = bass?.Clone(),
            highlightRoot = highlightRoot,
            color = color,
        };

        public void CopyFrom(UChordHelper other) {
            position = other.position;
            duration = other.duration;
            root = other.root;
            tones = other.tones.Select(tone => tone.Clone()).ToList();
            bass = other.bass?.Clone();
            highlightRoot = other.highlightRoot;
            color = other.color;
        }

        public void Normalize(bool is31Edo) {
            int divisions = is31Edo ? 31 : 12;
            position = Math.Max(0, position);
            duration = Math.Max(1, duration);
            root = Edo31.Mod(root, divisions);
            tones ??= new List<UChordInterval>();
            tones = tones
                .Where(tone => tone != null && tone.degree > 0)
                .GroupBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .Select(group => group.First().Clone())
                .OrderBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .ToList();
            if (bass != null && !tones.Any(tone => tone.Equals(bass))) {
                bass = null;
            }
            if (string.IsNullOrWhiteSpace(color)) {
                color = "#35A7D8";
            }
        }
    }

    public sealed class ChordHelperPreset {
        public string Name { get; }
        public IReadOnlyList<UChordInterval> Tones { get; }
        public ChordHelperPreset(string name, params UChordInterval[] tones) {
            Name = name;
            Tones = tones;
        }
    }

    public static class ChordHelperTheory {
        static readonly int[] Natural12 = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Natural31 = { 0, 5, 10, 13, 18, 23, 28 };
        static readonly string[] RelativeLabels12 = {
            "1", "♭2", "2", "♭3", "3", "4", "♯4", "5", "♭6", "6", "♭7", "7",
        };

        static UChordInterval I(int degree, int alteration = 0) => new UChordInterval(degree, alteration);

        public static IReadOnlyList<ChordHelperPreset> Presets { get; } = new[] {
            new ChordHelperPreset("Unison", I(1)),
            new ChordHelperPreset("Fifth", I(1), I(5)),
            new ChordHelperPreset("Major", I(1), I(3), I(5)),
            new ChordHelperPreset("Minor", I(1), I(3, -1), I(5)),
            new ChordHelperPreset("Diminished", I(1), I(3, -1), I(5, -1)),
            new ChordHelperPreset("Augmented", I(1), I(3), I(5, 1)),
            new ChordHelperPreset("Sus2", I(1), I(2), I(5)),
            new ChordHelperPreset("Sus4", I(1), I(4), I(5)),
            new ChordHelperPreset("Sixth", I(1), I(3), I(5), I(6)),
            new ChordHelperPreset("Minor sixth", I(1), I(3, -1), I(5), I(6)),
            new ChordHelperPreset("Dominant seventh", I(1), I(3), I(5), I(7, -1)),
            new ChordHelperPreset("Major seventh", I(1), I(3), I(5), I(7)),
            new ChordHelperPreset("Minor seventh", I(1), I(3, -1), I(5), I(7, -1)),
            new ChordHelperPreset("Half-diminished seventh", I(1), I(3, -1), I(5, -1), I(7, -1)),
            new ChordHelperPreset("Diminished seventh", I(1), I(3, -1), I(5, -1), I(7, -2)),
            new ChordHelperPreset("Harmonic seventh", I(1), I(3), I(5), I(6, 1)),
        };

        public static int IntervalOffset(int degree, int alteration, bool is31Edo) {
            if (degree <= 0) {
                throw new ArgumentOutOfRangeException(nameof(degree));
            }
            int[] naturals = is31Edo ? Natural31 : Natural12;
            int divisions = is31Edo ? 31 : 12;
            int zeroBased = degree - 1;
            return zeroBased / 7 * divisions + naturals[zeroBased % 7] + alteration * (is31Edo ? 2 : 1);
        }

        public static string IntervalLabel(int degree, int alteration) {
            string accidental = alteration switch {
                < 0 => new string('♭', -alteration),
                > 0 => new string('♯', alteration),
                _ => string.Empty,
            };
            return accidental + degree;
        }

        public static UChordInterval CanonicalInterval(int pitchClass, bool is31Edo) {
            int divisions = is31Edo ? 31 : 12;
            pitchClass = Edo31.Mod(pitchClass, divisions);
            if (!is31Edo) {
                return ParseLabel(RelativeLabels12[pitchClass]);
            }
            return ParseLabel(Edo31.RelativeScaleDegreeLabel(pitchClass));
        }

        static UChordInterval ParseLabel(string label) {
            int alteration = 0;
            int index = 0;
            while (index < label.Length && (label[index] == '♭' || label[index] == '♯')) {
                alteration += label[index++] == '♭' ? -1 : 1;
            }
            return new UChordInterval(int.Parse(label[index..]), alteration);
        }

        public static List<UChordInterval> CreatePreset(string name) {
            var preset = Presets.FirstOrDefault(candidate => candidate.Name == name)
                ?? throw new ArgumentException($"Unknown chord helper preset: {name}", nameof(name));
            return preset.Tones.Select(tone => tone.Clone()).ToList();
        }

        public static string QualityName(IEnumerable<UChordInterval> tones, bool is31Edo) {
            int divisions = is31Edo ? 31 : 12;
            var mask = tones.Select(tone => Edo31.Mod(tone.Offset(is31Edo), divisions)).Distinct().Order().ToArray();
            foreach (var preset in Presets) {
                var presetMask = preset.Tones.Select(tone => Edo31.Mod(tone.Offset(is31Edo), divisions)).Distinct().Order().ToArray();
                if (mask.SequenceEqual(presetMask)) {
                    return preset.Name;
                }
            }
            string members = string.Join(", ", tones
                .GroupBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .Select(group => group.First())
                .OrderBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .Select(tone => tone.Label));
            return $"Custom ({members})";
        }
    }
}
