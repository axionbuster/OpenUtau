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
    /// Harmonic annotation owned by a voice part. Position is relative to the
    /// part, like notes and curves. Transport preview stays separate from singer
    /// notes, phonemization, rendering, and export.
    /// </summary>
    public sealed class UChordHelper {
        public int position;
        public int duration = 480;
        public int root;
        public int? rootTone;
        public List<UChordInterval> tones = ChordHelperTheory.CreatePreset("Major");
        // Retains the selected analysis when one spelling has multiple valid names.
        public string? quality;
        public UChordInterval? bass;
        public bool highlightRoot = true;
        public bool mute;
        public string color = "#35A7D8";

        [YamlIgnore] public int End => position + duration;
        [YamlIgnore] public Guid PlaybackId { get; } = Guid.NewGuid();

        public UChordHelper Clone() => new UChordHelper {
            position = position,
            duration = duration,
            root = root,
            rootTone = rootTone,
            tones = tones.Select(tone => tone.Clone()).ToList(),
            quality = quality,
            bass = bass?.Clone(),
            highlightRoot = highlightRoot,
            mute = mute,
            color = color,
        };

        public void CopyFrom(UChordHelper other) {
            position = other.position;
            duration = other.duration;
            root = other.root;
            rootTone = other.rootTone;
            tones = other.tones.Select(tone => tone.Clone()).ToList();
            quality = other.quality;
            bass = other.bass?.Clone();
            highlightRoot = other.highlightRoot;
            mute = other.mute;
            color = other.color;
        }

        public void Normalize(bool is31Edo) {
            int divisions = is31Edo ? 31 : 12;
            position = Math.Max(0, position);
            duration = Math.Max(1, duration);
            root = Edo31.Mod(root, divisions);
            if (rootTone.HasValue) {
                int octave = (int)Math.Floor(rootTone.Value / (double)divisions);
                rootTone = Math.Clamp(octave, 0, 10) * divisions + root;
            }
            tones ??= new List<UChordInterval>();
            tones = tones
                .Where(tone => tone != null && tone.degree > 0)
                .GroupBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .Select(group => group.First().Clone())
                .OrderBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .ToList();
            if (!ChordHelperTheory.IsPresetSpelling(quality, tones)) {
                quality = null;
            }
            if (bass != null && !tones.Any(tone => tone.Equals(bass))) {
                bass = null;
            }
            if (string.IsNullOrWhiteSpace(color)) {
                color = "#35A7D8";
            }
        }
    }

    public sealed class UChordRegion {
        public Guid id = Guid.NewGuid();
        public int position;
        public int sourceDuration = 1920;
        public int duration = 1920;
        public List<UChordHelper> chordHelpers = new List<UChordHelper>();

        [YamlIgnore] public int End => (int)Math.Min(int.MaxValue, (long)position + duration);
        [YamlIgnore] public bool IsLooped => duration > sourceDuration;

        public UChordRegion Clone(bool newId = true) => new UChordRegion {
            id = newId ? Guid.NewGuid() : id,
            position = position,
            sourceDuration = sourceDuration,
            duration = duration,
            chordHelpers = chordHelpers.Select(helper => helper.Clone()).ToList(),
        };

        public void CopyFrom(UChordRegion other) {
            id = other.id;
            position = other.position;
            sourceDuration = other.sourceDuration;
            duration = other.duration;
            chordHelpers = other.chordHelpers.Select(helper => helper.Clone()).ToList();
        }

        public bool Normalize(bool is31Edo) {
            if (id == Guid.Empty) id = Guid.NewGuid();
            if (position < 0 || sourceDuration <= 0 || duration <= 0 ||
                (long)position + duration > int.MaxValue) return false;
            chordHelpers ??= new List<UChordHelper>();
            foreach (var helper in chordHelpers) helper.Normalize(is31Edo);
            return true;
        }
    }

    public readonly record struct ChordOccurrence(
        UChordRegion Region, UChordHelper Helper, int Iteration,
        int StartTick, int EndTick) {
        public Guid PlaybackId => ChordRegionPlaybackId.Create(Region.id, Helper.PlaybackId, Iteration);
    }

    public static class ChordRegionPlaybackId {
        public static Guid Create(Guid region, Guid helper, int iteration) {
            Span<byte> bytes = stackalloc byte[16];
            region.TryWriteBytes(bytes);
            Span<byte> helperBytes = stackalloc byte[16];
            helper.TryWriteBytes(helperBytes);
            for (int i = 0; i < 16; i++) bytes[i] ^= helperBytes[i];
            BitConverter.TryWriteBytes(bytes.Slice(12), iteration);
            return new Guid(bytes);
        }
    }

    public static class ChordRegionExpander {
        public const int MaxBreakRegions = 10000;

        public static IEnumerable<ChordOccurrence> Enumerate(
                UChordRegion region, int queryStart, int queryEnd) {
            if (queryEnd <= queryStart || region.sourceDuration <= 0 || region.duration <= 0) yield break;
            long regionStart = region.position;
            long regionEnd = Math.Min(int.MaxValue, regionStart + region.duration);
            long left = Math.Max(queryStart, regionStart);
            long right = Math.Min(queryEnd, regionEnd);
            if (right <= left) yield break;
            long firstIteration = Math.Max(0, (left - regionStart) / region.sourceDuration);
            long lastIteration = Math.Min((region.duration - 1L) / region.sourceDuration,
                (right - 1 - regionStart) / region.sourceDuration);
            for (long iteration = firstIteration; iteration <= lastIteration; iteration++) {
                long cycleStart = regionStart + iteration * region.sourceDuration;
                long cycleEnd = Math.Min(regionEnd, cycleStart + region.sourceDuration);
                foreach (var helper in region.chordHelpers) {
                    long start = cycleStart + helper.position;
                    long end = Math.Min(cycleEnd, start + Math.Max(0, helper.duration));
                    if (start < cycleEnd && end > start && start < right && end > left) {
                        yield return new ChordOccurrence(region, helper, (int)iteration, (int)start, (int)end);
                    }
                }
            }
        }

        public static bool TryBreak(UChordRegion region, out List<UChordRegion> pieces) {
            pieces = new List<UChordRegion>();
            if (region.sourceDuration <= 0 || region.duration <= region.sourceDuration) return false;
            long count = (region.duration + (long)region.sourceDuration - 1) / region.sourceDuration;
            if (count > MaxBreakRegions) return false;
            for (int i = 0; i < count; i++) {
                int length = (int)Math.Min(region.sourceDuration, region.duration - (long)i * region.sourceDuration);
                pieces.Add(new UChordRegion {
                    position = checked(region.position + i * region.sourceDuration),
                    sourceDuration = length, duration = length,
                    chordHelpers = region.chordHelpers.Select(helper => {
                        var clone = helper.Clone();
                        clone.duration = Math.Min(clone.duration, Math.Max(0, length - clone.position));
                        return clone;
                    }).Where(helper => helper.position < length && helper.duration > 0).ToList(),
                });
            }
            return true;
        }

        public static UChordRegion? CreateFromFreeHelpers(
                IEnumerable<UChordHelper> helpers, int selectionStart, int selectionEnd, int defaultPeriod) {
            var selected = helpers.Where(helper => selectionEnd > selectionStart
                    ? helper.position >= selectionStart && helper.position < selectionEnd
                    : true).ToList();
            if (selected.Count == 0) return null;
            int anchor = selectionEnd > selectionStart ? selectionStart : selected.Min(helper => helper.position);
            int end = selectionEnd > selectionStart
                ? Math.Max(selectionEnd, selected.Max(helper => helper.End))
                : selected.Max(helper => helper.End);
            int length = Math.Max(Math.Max(1, defaultPeriod), end - anchor);
            return new UChordRegion {
                position = anchor, sourceDuration = length, duration = length,
                chordHelpers = selected.Select(helper => {
                    var clone = helper.Clone(); clone.position -= anchor; return clone;
                }).ToList(),
            };
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
        static readonly int[] NaturalDegreeFifths = { 0, 2, 4, -1, 1, 3, 5 };
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
            new ChordHelperPreset("Add ninth", I(1), I(3), I(5), I(9)),
            new ChordHelperPreset("Dominant ninth", I(1), I(3), I(5), I(7, -1), I(9)),
            new ChordHelperPreset("Major ninth", I(1), I(3), I(5), I(7), I(9)),
            new ChordHelperPreset("Minor ninth", I(1), I(3, -1), I(5), I(7, -1), I(9)),
            new ChordHelperPreset("Dominant eleventh", I(1), I(3), I(5), I(7, -1), I(9), I(11)),
            new ChordHelperPreset("Dominant thirteenth", I(1), I(3), I(5), I(7, -1), I(9), I(11), I(13)),
            new ChordHelperPreset("Harmonic seventh", I(1), I(3), I(5), I(6, 1)),
            new ChordHelperPreset("Italian augmented sixth", I(1), I(3), I(6, 1)),
            new ChordHelperPreset("French augmented sixth", I(1), I(3), I(4, 1), I(6, 1)),
            new ChordHelperPreset("German augmented sixth", I(1), I(3), I(5), I(6, 1)),
            new ChordHelperPreset("Japanese augmented sixth", I(1), I(2), I(4, 1), I(6, 1)),
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

        public static UChordInterval DisplayInterval(
            UChordInterval interval, IEnumerable<UChordInterval> chordTones, bool is31Edo) {
            var display = interval.Clone();
            if (display.degree > 7) {
                return display;
            }
            int divisions = is31Edo ? 31 : 12;
            int pitchClass = Edo31.Mod(display.Offset(is31Edo), divisions);
            if (is31Edo && display.degree == 1 && display.alteration < 0 && pitchClass >= 27) {
                display.degree = 8;
                return display;
            }
            bool hasThird = chordTones.Any(tone => SimpleDegree(tone.degree) == 3);
            bool hasSeventh = chordTones.Any(tone => SimpleDegree(tone.degree) == 7);
            int simpleDegree = SimpleDegree(display.degree);
            if (hasThird && simpleDegree is 2 or 4) {
                display.degree += 7;
            } else if (hasSeventh && simpleDegree == 6) {
                display.degree += 7;
            }
            return display;
        }

        static int SimpleDegree(int degree) => Edo31.Mod(degree - 1, 7) + 1;

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

        static bool SameSpelling(IEnumerable<UChordInterval> left, IEnumerable<UChordInterval> right) =>
            left.OrderBy(tone => tone.degree).ThenBy(tone => tone.alteration)
                .SequenceEqual(right.OrderBy(tone => tone.degree).ThenBy(tone => tone.alteration));

        public static bool IsPresetSpelling(string? name, IEnumerable<UChordInterval> tones) =>
            name != null && Presets.Any(preset => preset.Name == name && SameSpelling(tones, preset.Tones));

        public static string QualityName(
                IEnumerable<UChordInterval> tones, bool is31Edo, string? preferredQuality = null) {
            var toneList = tones.ToList();
            var spelledMatches = Presets.Where(preset => SameSpelling(toneList, preset.Tones)).ToList();
            if (preferredQuality != null && spelledMatches.Any(preset => preset.Name == preferredQuality)) {
                return preferredQuality;
            }
            if (spelledMatches.Count > 0) {
                return spelledMatches[0].Name;
            }
            int divisions = is31Edo ? 31 : 12;
            var mask = toneList.Select(tone => Edo31.Mod(tone.Offset(is31Edo), divisions)).Distinct().Order().ToArray();
            foreach (var preset in Presets) {
                var presetMask = preset.Tones.Select(tone => Edo31.Mod(tone.Offset(is31Edo), divisions)).Distinct().Order().ToArray();
                if (mask.SequenceEqual(presetMask)) {
                    return preset.Name;
                }
            }
            string members = string.Join(", ", toneList
                .GroupBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .Select(group => group.First())
                .OrderBy(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                .Select(tone => DisplayInterval(tone, toneList, is31Edo).Label));
            return $"Custom ({members})";
        }

        public static string ChordName(UChordHelper helper, bool is31Edo, int preferredFifths) {
            int divisions = is31Edo ? 31 : 12;
            int root = Edo31.Mod(helper.root, divisions);
            string rootName = is31Edo
                ? Edo31.FifthName(Edo31.FifthsForStep(root, preferredFifths))
                : MusicMath.KeysInOctave[root].Item1;
            string quality = QualityName(helper.tones, is31Edo, helper.quality);
            string suffix = quality switch {
                "Unison" => " (unison)",
                "Fifth" => "5",
                "Major" => string.Empty,
                "Minor" => "m",
                "Diminished" => "dim",
                "Augmented" => "aug",
                "Sus2" => "sus2",
                "Sus4" => "sus4",
                "Sixth" => "6",
                "Minor sixth" => "m6",
                "Dominant seventh" => "7",
                "Major seventh" => "maj7",
                "Minor seventh" => "m7",
                "Half-diminished seventh" => "m7♭5",
                "Diminished seventh" => "dim7",
                "Add ninth" => "add9",
                "Dominant ninth" => "9",
                "Major ninth" => "maj9",
                "Minor ninth" => "m9",
                "Dominant eleventh" => "11",
                "Dominant thirteenth" => "13",
                "Harmonic seventh" => "7:4",
                "Italian augmented sixth" => "It+6",
                "French augmented sixth" => "Fr+6",
                "German augmented sixth" => "Ger+6",
                "Japanese augmented sixth" => "Jp+6",
                _ => "...",
            };
            string name = rootName + suffix;
            if (helper.bass != null && Edo31.Mod(helper.bass.Offset(is31Edo), divisions) != 0) {
                string bassName;
                if (is31Edo) {
                    int rootFifths = Edo31.FifthsForStep(root, preferredFifths);
                    int degree = Edo31.Mod(helper.bass.degree - 1, 7) + 1;
                    int bassFifths = rootFifths + NaturalDegreeFifths[degree - 1] + helper.bass.alteration * 7;
                    bassName = Edo31.FifthName(bassFifths);
                } else {
                    int bass = Edo31.Mod(root + helper.bass.Offset(false), divisions);
                    bassName = MusicMath.KeysInOctave[bass].Item1;
                }
                name += "/" + bassName;
            }
            return name;
        }
    }
}
