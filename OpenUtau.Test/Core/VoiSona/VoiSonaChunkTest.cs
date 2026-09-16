using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.VoiSona;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaChunkTest {
        static UProject Fixture(int count = 44, bool tempoChange = true, VoiSonaSinger? singer = null, int restAfter = -1) {
            var project = Format.Ustx.Create(); project.Is31Edo = true;
            project.tempos[0].bpm = 120;
            if (tempoChange) project.tempos.Add(new UTempo(7 * 480, 137));
            var track = project.tracks[0];
            track.Singer = singer ?? new VoiSonaSinger("ja_JP", "2.1.0", "/unused");
            track.Phonemizer = new DefaultPhonemizer(); track.RendererSettings.renderer = Renderers.VOISONA;
            var part = new UVoicePart { position = 17 }; project.parts.Add(part);
            for (int i = 0; i < count; i++) {
                var note = project.CreateGridNote(173 + i % 4, i * 480 + (restAfter >= 0 && i > restAfter ? 240 : 0), 480);
                note.lyric = i is 23 or 24 or 25 ? "+" : "あ";
                part.notes.Add(note);
            }
            part.duration = part.notes.Last().End;
            VoiSonaLyricsTest.Phonemize(project);
            return project;
        }
        [Fact]
        public void LongLegatoSplitsWithCompleteContextAndExactTimelineCoverage() {
            var project = Fixture(); var part = (UVoicePart)project.parts[0];
            var phrases = part.renderPhrases;
            Assert.Equal(2, phrases.Count);
            var left = phrases[0].VoiSonaChunk!; var right = phrases[1].VoiSonaChunk!;
            Assert.Equal(left.EndFrame, right.StartFrame);
            Assert.Equal(VoiSonaChunk.Frame(project.timeAxis.TickPosToMsPos(part.position) - 500), left.StartFrame);
            Assert.Equal(VoiSonaChunk.Frame(project.timeAxis.TickPosToMsPos(part.position + part.duration) + 500), right.EndFrame);
            foreach (var phrase in phrases) {
                var notes = phrase.notes.Where(n => n.endMs > phrase.positionMs && n.positionMs < phrase.endMs).ToArray();
                Assert.False(notes[0].lyric.StartsWith('+'));
                Assert.All(notes, n => Assert.Equal(part.notes.Single(saved => project.timeAxis.TickPosToMsPos(part.position + saved.position) == n.positionMs).AdjustedTone, n.adjustedTone));
                Assert.NotNull(VoiSonaState.FromPhrase(phrase, (VoiSonaSinger)project.tracks[0].Singer));
            }
            Assert.Equal(44, part.notes.Count); // source notes and lyrics are untouched
            Assert.Equal(3, part.notes.Count(n => n.lyric == "+"));
        }
        [Fact]
        public void CroppedContextCrossfadesSumToUnityOnFractionalSampleBoundaries() {
            var phrases = ((UVoicePart)Fixture().parts[0]).renderPhrases;
            var output = phrases.Select(p => {
                var full = VoiSonaRenderer.FullLayout(p);
                full.samples = Enumerable.Repeat(1f, (int)Math.Ceiling(full.estimatedLengthMs * 44.1)).ToArray();
                return p.VoiSonaChunk!.Crop(full);
            }).ToArray();
            int first = phrases.First().VoiSonaChunk!.OutputStart;
            int last = phrases.Last().VoiSonaChunk!.OutputEnd;
            var sum = new float[last - first];
            foreach (var result in output) {
                var slot = new SignalChain.SampleSlot(result.positionMs - result.leadingMs, result.estimatedLengthMs, 1);
                int start = slot.Offset;
                Assert.Equal(result.samples.Length, slot.EstimatedLength);
                for (int i = 0; i < result.samples.Length; i++) sum[start - first + i] += result.samples[i];
            }
            Assert.All(sum, value => Assert.Equal(1f, value, 6));
        }
        [Fact]
        public void ExtensionChainIsNeverCutAtTargetAndShortPhrasesKeepLegacyLayout() {
            var project = Fixture(count: 28, tempoChange: false);
            var phrases = ((UVoicePart)project.parts[0]).renderPhrases;
            Assert.Equal(2, phrases.Count);
            // 12 seconds lands inside the + chain, so ownership starts at note 26.
            Assert.Equal(VoiSonaChunk.Frame(project.timeAxis.TickPosToMsPos(17 + 26 * 480)), phrases[1].VoiSonaChunk!.StartFrame);
            var shortPhrase = Assert.Single(((UVoicePart)Fixture(count: 4).parts[0]).renderPhrases);
            Assert.Null(shortPhrase.VoiSonaChunk);
            Assert.Equal(500, shortPhrase.renderer.Layout(shortPhrase).leadingMs);
        }
        [Fact]
        public void ShortMergedRestIsKeptInsideContextAndNeverUsedAsArtificialSeam() {
            var project = Fixture(44, false, restAfter: 25);
            var part = (UVoicePart)project.parts[0];
            Assert.Equal(2, part.renderPhrases.Count);
            var left = part.renderPhrases[0]; var right = part.renderPhrases[1];
            // The target lies in extensions ending with a short rest. Defer the
            // cut to the next adjacent lyric, keeping the complete release/rest.
            Assert.Equal(VoiSonaChunk.Frame(project.timeAxis.TickPosToMsPos(17 + 27 * 480 + 240)), right.VoiSonaChunk!.StartFrame);
            Assert.Equal(left.VoiSonaChunk!.EndFrame, right.VoiSonaChunk.StartFrame);
            Assert.Contains(left.notes, n => n.positionMs == project.timeAxis.TickPosToMsPos(17 + 26 * 480 + 240));
            Assert.True(left.endMs > project.timeAxis.TickPosToMsPos(17 + 26 * 480 + 240));
        }
        [InstalledVoiSonaFact]
        public async Task TwoChunksProduceAudibleJoinedAudioAndStableCache() {
            string artifacts = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS")
                ?? Path.Combine(Path.GetTempPath(), "voisona-chunk-artifacts");
            Directory.CreateDirectory(artifacts);
            var singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Single(s => s.NoteLanguage == 1);
            var project = Fixture(32, false, singer);
            var part = (UVoicePart)project.parts[0]; Assert.Equal(2, part.renderPhrases.Count);
            var timings = new double[2]; var audio = new RenderResult[2];
            using var cancel = new CancellationTokenSource();
            for (int i = 0; i < 2; i++) {
                var phrase = part.renderPhrases[i];
                var score = VoiSonaState.FromPhrase(phrase, singer);
                await File.WriteAllTextAsync(Path.Combine(artifacts, $"chunk-{i}-request.json"),
                    JsonConvert.SerializeObject(new {state = score.State, durationMs = score.DurationMs, ownership = phrase.VoiSonaChunk}));
                var watch = Stopwatch.StartNew();
                audio[i] = await phrase.renderer.Render(phrase, new Progress(phrase.phones.Length), 0, cancel);
                timings[i] = watch.Elapsed.TotalSeconds;
                var cached = await phrase.renderer.Render(phrase, new Progress(phrase.phones.Length), 0, cancel);
                Assert.Equal(audio[i].samples, cached.samples);
            }
            int first = part.renderPhrases[0].VoiSonaChunk!.OutputStart;
            int last = part.renderPhrases[1].VoiSonaChunk!.OutputEnd;
            var joined = new float[last - first];
            for (int i = 0; i < 2; i++) {
                int offset = part.renderPhrases[i].VoiSonaChunk!.OutputStart - first;
                for (int j = 0; j < audio[i].samples.Length; j++) joined[offset + j] += audio[i].samples[j];
            }
            int seam = part.renderPhrases[0].VoiSonaChunk!.EndFrame - first;
            double rms = Math.Sqrt(joined.Skip(seam - 2205).Take(4410).Average(x => (double)x*x));
            Assert.True(rms > 0.005);
            Assert.All(joined, value => Assert.True(float.IsFinite(value)));
            using (var writer = new NAudio.Wave.WaveFileWriter(Path.Combine(artifacts, "joined.wav"),
                    NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 1))) writer.WriteSamples(joined, 0, joined.Length);
            await File.WriteAllTextAsync(Path.Combine(artifacts, "timings.json"), JsonConvert.SerializeObject(new {
                timings, seamSeconds = seam / 44100.0, seamRms = rms,
                sourceSeconds = project.timeAxis.MsBetweenTickPos(part.position, part.position + part.duration) / 1000, chunkSeconds = part.renderPhrases.Select(p => p.durationMs / 1000).ToArray(),
            }, Formatting.Indented));
        }
    }
}
