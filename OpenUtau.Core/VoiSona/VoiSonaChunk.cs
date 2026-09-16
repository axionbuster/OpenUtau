using System;
using System.Collections.Generic;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.VoiSona {
    // Each chunk owns a short timeline interval, but synthesizes one complete
    // syllable of context on either side. Never cut an extension from its head.
    internal sealed record VoiSonaChunk(int StartFrame, int EndFrame, bool FadeIn, bool FadeOut) {
        internal const double TargetMs = 12_000;
        internal const int HalfFadeFrames = 441; // 20 ms complementary linear crossfade.
        internal static int Frame(double ms) => (int)Math.Floor(ms * 44.1);
        // SampleSlot truncates doubles. A sub-nanosecond bias prevents binary
        // division/multiplication rounding from placing an integer frame early.
        internal static double Milliseconds(int frame) => frame / 44.1 + Math.Sign(frame) * 1e-8;
        internal int OutputStart => StartFrame - (FadeIn ? HalfFadeFrames : 0);
        internal int OutputEnd => EndFrame + (FadeOut ? HalfFadeFrames : 0);
        internal RenderResult Layout() => new() {
            positionMs = Milliseconds(OutputStart), leadingMs = 0,
            estimatedLengthMs = Milliseconds(OutputEnd - OutputStart),
        };
        internal RenderResult Crop(RenderResult source) {
            var result = Layout();
            result.samples = new float[OutputEnd - OutputStart];
            int sourceStart = Frame(source.positionMs - source.leadingMs);
            int first = OutputStart - sourceStart;
            if (first < 0 || first + result.samples.Length > source.samples.Length)
                throw new InvalidOperationException("VoiSona chunk context does not cover its output interval.");
            Array.Copy(source.samples, first, result.samples, 0, result.samples.Length);
            for (int i = 0; i < result.samples.Length; i++) {
                int frame = OutputStart + i;
                double weight = 1;
                if (FadeIn) weight = Math.Min(weight, (frame - OutputStart) / (2.0 * HalfFadeFrames));
                if (FadeOut) weight = Math.Min(weight, (OutputEnd - frame) / (2.0 * HalfFadeFrames));
                result.samples[i] *= (float)weight;
            }
            return result;
        }
        internal static IEnumerable<RenderPhrase> Build(PhraseSource source, int start, int end) {
            var boundaries = new List<int> { start };
            for (int i = start + 1; i < end; i++) {
                var current = source.Phonemes[i];
                var previous = source.Phonemes[i - 1];
                var note = source.Notes[current.NoteIndex];
                // A phonemizer may emit multiple phones per lyric. Keep them
                // together as well as all notes belonging to that syllable.
                if (previous.End == current.Position
                        && current.NoteIndex != previous.NoteIndex && note.Extends == -1
                        && !note.Lyric.StartsWith('+')
                        && current.PositionMs - source.Phonemes[boundaries[^1]].PositionMs >= TargetMs) {
                    boundaries.Add(i);
                }
            }
            boundaries.Add(end);
            if (boundaries.Count == 2) {
                yield return new RenderPhrase(source, start, end);
                yield break;
            }
            for (int i = 0; i < boundaries.Count - 1; i++) {
                int a = boundaries[i], b = boundaries[i + 1];
                int contextStart = a, contextEnd = b;
                if (a > start) {
                    contextStart--;
                    int head = source.Phonemes[contextStart].NoteIndex;
                    if (source.Notes[head].Extends >= 0) head = source.Notes[head].Extends;
                    while (contextStart > start && source.Phonemes[contextStart - 1].NoteIndex >= head) contextStart--;
                }
                if (b < end) {
                    int head = source.Phonemes[b].NoteIndex;
                    while (contextEnd < end && (source.Phonemes[contextEnd].NoteIndex == head
                            || source.Notes[source.Phonemes[contextEnd].NoteIndex].Extends == head)) contextEnd++;
                }
                var chunk = new VoiSonaChunk(
                    Frame(source.Phonemes[a].PositionMs - (a == start ? VoiSonaState.HeadMs : 0)),
                    Frame(source.Phonemes[b - 1].EndMs + (b == end ? VoiSonaState.TailMs : 0)),
                    a != start, b != end);
                yield return new RenderPhrase(source, contextStart, contextEnd, chunk);
            }
        }
    }
}
