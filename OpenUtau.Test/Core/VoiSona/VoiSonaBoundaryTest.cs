using System;
using System.Linq;
using System.IO;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.VoiSona;
using Xunit;

namespace OpenUtau.Core {
    public class VoiSonaBoundaryTest {
        [Fact]
        public void EarlySustainsFirstSyllableUntilPlainPlus() {
            var notes = VoiSonaState.EnglishNotes(new[] {
                new VoiSonaNote(500, 250, 69, "early"),
                new VoiSonaNote(750, 250, 70, "+~"),
                new VoiSonaNote(1000, 500, 71, "+"),
            });
            Assert.Equal(2, notes.Count);
            Assert.Equal("axr", notes[0].Phoneme);
            Assert.Equal(500, notes[0].DurationMs);
            Assert.Equal(1000, notes[1].StartMs);
            Assert.Equal("l,iy", notes[1].Phoneme);
            Assert.Equal(71, notes[1].Tone);
        }
        [Theory]
        [InlineData("+~")]
        [InlineData("+*")]
        public void VowelExtensionDoesNotAdvanceToNextSyllable(string extension) {
            var notes = VoiSonaState.EnglishNotes(new[] {
                new VoiSonaNote(500, 250, 69, "proudly"),
                new VoiSonaNote(750, 250, 70, extension),
                new VoiSonaNote(1000, 500, 71, "+"),
                new VoiSonaNote(1500, 500, 72, "+"),
            });
            Assert.Equal(2, notes.Count);
            Assert.Equal("p,r,aw", notes[0].Phoneme);
            Assert.Equal("d,l,iy", notes[1].Phoneme);
            Assert.Equal(1000, notes[1].DurationMs);
        }
        [Fact]
        public void MonosyllableAndUnmarkedWordsKeepNativePronunciation() {
            var notes = VoiSonaState.EnglishNotes(new[] {
                new VoiSonaNote(500, 250, 69, "oh"), new VoiSonaNote(750, 250, 70, "+"),
                new VoiSonaNote(1000, 500, 71, "early"),
            });
            Assert.Equal(2, notes.Count);
            Assert.All(notes, n => Assert.Null(n.Phoneme));
            Assert.Equal(500, notes[0].DurationMs);
        }
        [Fact]
        public void EnglishExtensionsCannotBridgeRestsOrLackHead() {
            Assert.Throws<ArgumentException>(() => VoiSonaState.EnglishNotes(new[] {new VoiSonaNote(0, 500, 69, "+")}));
            Assert.Throws<ArgumentException>(() => VoiSonaState.EnglishNotes(new[] {
                new VoiSonaNote(0, 500, 69, "early"), new VoiSonaNote(1000, 500, 70, "+"),
            }));
        }
        [Fact]
        public void ExplicitHumHintSurvivesExtensionsAndReachesNativeState() {
            var input = new[] { new VoiSonaNote(500, 500, 69, "hmm [mm]"), new VoiSonaNote(1000, 500, 70, "+~") };
            var note = Assert.Single(VoiSonaState.EnglishNotes(input));
            Assert.Equal("mm", note.Phoneme);
            Assert.Equal("hmm", note.Lyric);
            Assert.Equal(1000, note.DurationMs);
            var state = VoiSonaState.Build(new VoiSonaSinger("en_US", "test", "/unused"), input, 2000, _ => 69);
            Assert.Contains("Phoneme", System.Text.Encoding.UTF8.GetString(state.State));
            Assert.Contains("PastAnalyzedPhoneme", System.Text.Encoding.UTF8.GetString(state.State));
            Assert.Contains("AnalyzedNoteLanguage", System.Text.Encoding.UTF8.GetString(state.State));
            Assert.DoesNotContain("[mm]", System.Text.Encoding.UTF8.GetString(state.State));
        }
        [InstalledVoiSonaFact]
        public async Task EnglishSyllablesAndHumRender() {
            string root = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS") ?? Path.Combine(Path.GetTempPath(), "voisona-boundary-artifacts");
            Directory.CreateDirectory(root);
            var singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Single(s => s.NoteLanguage == 2);
            var notes = new[] {
                new VoiSonaNote(500, 500, 65, "early"), new VoiSonaNote(1000, 500, 66, "+~"),
                new VoiSonaNote(1500, 750, 67, "+"), new VoiSonaNote(2750, 1500, 65, "hmm [mm]"),
            };
            var score = VoiSonaState.Build(singer, notes, 5000, _ => 65);
            var job = new VoiSonaJob { state = score.State, durationMs = score.DurationMs,
                output = Path.Combine(root, "early-hum.wav"), result = Path.Combine(root, "result.json"),
                cancel = Path.Combine(root, "cancel"),
                noteWindows = notes.Select(n => new[] {n.StartMs, n.StartMs + n.DurationMs}).ToArray() };
            await VoiSonaRenderer.RunHelper(VoiSonaRenderer.HelperPath, job, root, CancellationToken.None);
            var samples = VoiSonaRenderer.TryReadAudio(job.output, 220500);
            Assert.NotNull(samples);
            Assert.True(samples!.Skip(3 * 44100).Take(44100).Any(s => Math.Abs(s) > .005f));
        }
        [Fact]
        public async Task ExportsOvertakePlaybackAndCanceledWaitersDoNotLeakSlots() {
            var gate = new VoiSonaRenderGate(1);
            await gate.WaitAsync(false, CancellationToken.None);
            using var cancel = new CancellationTokenSource();
            var canceled = gate.WaitAsync(true, cancel.Token);
            var playback = gate.WaitAsync(false, CancellationToken.None);
            var export = gate.WaitAsync(true, CancellationToken.None);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            gate.Release();
            await export.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(playback.IsCompleted);
            gate.Release();
            await playback.WaitAsync(TimeSpan.FromSeconds(2));
            gate.Release();
            await gate.WaitAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            gate.Release();
        }
    }
}
