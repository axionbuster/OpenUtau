using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.VoiSona {
    // JUCE ValueTree stream, used by the AU's public fullState property.
    // We generate musical state only: never copy authentication or an existing song.
    internal sealed class VoiSonaTree {
        public string Name;
        public Dictionary<string, object> Attributes = new();
        public List<VoiSonaTree> Children = new();
        public VoiSonaTree(string name, params (string, object)[] attributes) {
            Name = name;
            foreach (var (key, value) in attributes) Attributes.Add(key, value);
        }
        public VoiSonaTree Add(params VoiSonaTree[] children) { Children.AddRange(children); return this; }
        public byte[] Encode() {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            Write(writer);
            return stream.ToArray();
        }
        static void String(BinaryWriter writer, string value) {
            if (value.Contains('\0')) throw new ArgumentException("VoiSona text contains a null character.");
            writer.Write(Encoding.UTF8.GetBytes(value)); writer.Write((byte)0);
        }
        static void Count(BinaryWriter writer, int value) {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            int bytes = value == 0 ? 0 : value <= 255 ? 1 : value <= 65535 ? 2 : value <= 16777215 ? 3 : 4;
            writer.Write((byte)bytes);
            for (int i = 0; i < bytes; i++) writer.Write((byte)(value >> (8 * i)));
        }
        void Write(BinaryWriter writer) {
            String(writer, Name); Count(writer, Attributes.Count);
            foreach (var (key, value) in Attributes) {
                String(writer, key);
                switch (value) {
                    case bool b: Count(writer, 1); writer.Write((byte)(b ? 2 : 3)); break;
                    case int n: Count(writer, 5); writer.Write((byte)1); writer.Write(n); break;
                    case double d: Count(writer, 9); writer.Write((byte)4); writer.Write(d); break;
                    case string s: Count(writer, Encoding.UTF8.GetByteCount(s) + 2); writer.Write((byte)5); String(writer, s); break;
                    default: throw new ArgumentException("Unsupported VoiSona state value.");
                }
            }
            Count(writer, Children.Count);
            foreach (var child in Children) child.Write(writer);
        }
    }

    internal sealed record VoiSonaNote(double StartMs, double DurationMs, double Tone, string Lyric);
    internal sealed record VoiSonaScore(byte[] State, double DurationMs);

    internal static class VoiSonaState {
        public const int Schema = 2;
        public const double HeadMs = 500;
        public const double TailMs = 500;
        public static double LogF0(double tone) => Math.Log(440) + (tone - 69) * Math.Log(2) / 12;
        public static VoiSonaScore FromPhrase(RenderPhrase phrase, VoiSonaSinger singer) {
            var notes = phrase.notes.Where(n => n.endMs > phrase.positionMs && n.positionMs < phrase.endMs)
                .Select(n => new VoiSonaNote(n.positionMs - phrase.positionMs + HeadMs, n.durationMs,
                    n.adjustedTone, Lyric(n))).ToArray();
            double length = Math.Max(phrase.durationMs + HeadMs + TailMs, notes.Max(n => n.StartMs + n.DurationMs) + TailMs);
            return Build(singer, notes, length, ms => {
                double tick = phrase.timeAxis.MsPosToTickPos(ms - HeadMs + phrase.positionMs);
                double index = (tick - phrase.position + phrase.leading) / 5;
                int left = Math.Clamp((int)Math.Floor(index), 0, phrase.pitches.Length - 1);
                int right = Math.Min(left + 1, phrase.pitches.Length - 1);
                double fraction = Math.Clamp(index - left, 0, 1);
                return (phrase.pitches[left] * (1 - fraction) + phrase.pitches[right] * fraction) / 100;
            });
        }
        internal static string Lyric(RenderNote note) {
            if (note.lyric.StartsWith('+')) return note.lyric;
            if (note.phonemes.Length != 1)
                throw new ArgumentException("VoiSona requires one lyric per note. Use DEFAULT for Japanese/English lyrics or KO to JA for Korean lyrics with the Japanese voice.");
            string lyric = note.phonemes[0];
            if (lyric.Any(c => c >= '\uac00' && c <= '\ud7a3'))
                throw new ArgumentException("VoiSona cannot sing Hangul directly. Select KO to JA with the Japanese voice to convert Korean lyrics.");
            return lyric;
        }
        internal static IReadOnlyList<VoiSonaNote> MergeExtensions(IReadOnlyList<VoiSonaNote> notes) {
            var merged = new List<VoiSonaNote>();
            foreach (var note in notes) {
                if (note.Lyric.StartsWith('+')) {
                    if (merged.Count == 0 || Math.Abs(merged[^1].StartMs + merged[^1].DurationMs - note.StartMs) > 0.1)
                        throw new ArgumentException("VoiSona extension notes must follow a lyric without a gap.");
                    var previous = merged[^1];
                    merged[^1] = previous with { DurationMs = note.StartMs + note.DurationMs - previous.StartMs };
                } else merged.Add(note);
            }
            return merged;
        }
        public static VoiSonaScore Build(VoiSonaSinger singer, IReadOnlyList<VoiSonaNote> notes,
                double lengthMs, Func<double, double> pitch) {
            if (notes.Count == 0 || !double.IsFinite(lengthMs) || lengthMs <= 0 || lengthMs > 600_000)
                throw new ArgumentException("VoiSona phrases must contain notes and be at most ten minutes long.");
            var score = new VoiSonaTree("Score", ("PhonemeSeparatedBySyllable", true));
            double previousEnd = -1;
            foreach (var n in MergeExtensions(notes)) {
                if (!double.IsFinite(n.StartMs) || !double.IsFinite(n.DurationMs) || !double.IsFinite(n.Tone)
                        || n.StartMs < 0 || n.DurationMs <= 0 || n.StartMs < previousEnd - 0.1)
                    throw new ArgumentException("VoiSona requires non-overlapping notes with positive durations.");
                previousEnd = n.StartMs + n.DurationMs;
                int midi = (int)Math.Round(n.Tone);
                if (midi < 0 || midi > 127) throw new ArgumentException("VoiSona note is outside MIDI range 0–127.");
                string lyric = n.Lyric;
                if (string.IsNullOrWhiteSpace(lyric)) throw new ArgumentException("VoiSona needs a lyric on each note.");
                score.Add(new VoiSonaTree("Note", ("Clock", (int)Math.Round(n.StartMs * 1.92)),
                    ("Duration", Math.Max(1, (int)Math.Round(n.DurationMs * 1.92))),
                    ("PitchStep", midi % 12), ("PitchOctave", midi / 12 - 1), ("Lyric", lyric),
                    ("Syllabic", 0), ("NoteLanguage", singer.NoteLanguage), ("DoReMi", false)));
            }
            int frames = (int)Math.Ceiling(lengthMs / 5);
            var f0 = new VoiSonaTree("LogF0", ("Length", frames));
            for (int i = 0; i < frames; i++) {
                double value = LogF0(pitch(i * 5));
                if (!double.IsFinite(value)) throw new ArgumentException("VoiSona pitch must be finite.");
                f0.Add(new VoiSonaTree("Data", ("Index", i), ("Repeat", 1), ("Value", value)));
            }
            var state = new VoiSonaTree("StateInformation", ("TempoSync", false)).Add(
                new VoiSonaTree("VoiceInformation", ("CharacterName", "Chis-A"),
                    ("VoiceFileName", singer.VoiceFileName), ("Language", singer.Language),
                    ("VoiceVersion", singer.Version)).Add(new VoiSonaTree("EmotionList").Add(
                        new VoiSonaTree("Emotion", ("Label", "Normal"), ("Ratio", 1.0)))),
                new VoiSonaTree("GlobalParameters", ("GlobalTune", 1.0), ("GlobalVibAmp", 0.0),
                    ("GlobalVibFrq", 1.0), ("GlobalAlpha", 0.0), ("GlobalHusky", 0.0)),
                new VoiSonaTree("Song").Add(
                    new VoiSonaTree("Tempo").Add(new VoiSonaTree("Sound", ("Clock", 0), ("Tempo", 120.0))),
                    new VoiSonaTree("Beat").Add(new VoiSonaTree("Time", ("Clock", 0), ("Beats", 4), ("BeatType", 4))), score),
                new VoiSonaTree("SignerConfig", ("RomajiMode", false), ("DefaultNoteLanguage", singer.NoteLanguage)),
                new VoiSonaTree("Parameter").Add(f0, new VoiSonaTree("VibAmp", ("Length", frames)).Add(
                    new VoiSonaTree("Data", ("Index", 0), ("Repeat", frames), ("Value", 0.0)))));
            return new VoiSonaScore(state.Encode(), lengthMs);
        }
    }
}
