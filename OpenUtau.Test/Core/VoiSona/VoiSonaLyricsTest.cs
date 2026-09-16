using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.VoiSona;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaLyricsTest {
        internal static void Phonemize(UProject project) {
            var previous = DocManager.Inst.TakeProjectForTest(project);
            try {
                project.ValidateFull();
                foreach (var part in project.parts.OfType<UVoicePart>()) {
                    var track = project.tracks[part.trackNo];
                    var notes = part.notes.ToArray();
                    var starts = Enumerable.Range(0, notes.Length).Where(i => notes[i].Extends == null).ToArray();
                    var groups = starts.Select(i => notes.Skip(i).TakeWhile((n, j) => j == 0 || n.Extends == notes[i])
                        .Select(n => n.ToPhonemizerNote(track, part)).ToArray()).ToArray();
                    var request = new PhonemizerRequest {
                        singer = track.Singer, part = part, timestamp = part.GetRenderRequest().timestamp,
                        noteIndexes = starts, notes = groups, phonemizers = new[] { track.Phonemizer },
                        notePhonemizerIndices = new int[groups.Length], timeAxis = project.timeAxis.Clone(),
                    };
                    var response = (PhonemizerResponse)typeof(PhonemizerRunner)
                        .GetMethod("Phonemize", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] {request})!;
                    part.SetPhonemizerResponse(response);
                    part.Validate(new ValidateOptions { SkipPhonemizer = true }, project, track);
                    Assert.True(part.PhonemesUpToDate);
                    Assert.DoesNotContain(part.phonemes, p => p.Error);
                    Assert.NotEmpty(part.renderPhrases);
                }
            } finally { DocManager.Inst.TakeProjectForTest(previous); }
        }
        static UProject KoreanFixture() {
            var project = Format.Ustx.Create(); project.Is31Edo = true;
            var track = project.tracks[0];
            track.Singer = new VoiSonaSinger("ja_JP", "2.1.0", "/unused");
            track.Phonemizer = new KOtoJAPhonemizer(); track.RendererSettings.renderer = Renderers.VOISONA;
            var part = new UVoicePart(); project.parts.Add(part);
            string[] lyrics = {"돌", "봐", "+", "주"};
            for (int i = 0; i < lyrics.Length; i++) {
                var note = project.CreateGridNote(173 + i, i * 480, 480);
                note.lyric = lyrics[i]; part.notes.Add(note);
            }
            part.duration = 1920;
            return project;
        }
        [Fact]
        public void KoreanConversionReachesNativeStateWithoutChangingSavedLyrics() {
            var project = KoreanFixture(); Phonemize(project);
            var part = (UVoicePart)project.parts[0];
            Assert.Equal(new[] {"돌", "봐", "+", "주"}, part.notes.Select(n => n.lyric));
            var phrase = Assert.Single(part.renderPhrases);
            var score = VoiSonaState.FromPhrase(phrase, (VoiSonaSinger)project.tracks[0].Singer);
            string state = Encoding.UTF8.GetString(score.State);
            Assert.Contains("どる", state); Assert.DoesNotContain("돌", state);
            Assert.DoesNotContain(part.phonemes, p => p.phoneme.Contains(' '));
            Assert.Equal(173 * 12.0 / 31, phrase.notes[0].adjustedTone, 4);
        }
        [Fact]
        public void UnconvertedHangulFailsInsteadOfRenderingNoise() {
            var project = KoreanFixture(); project.tracks[0].Phonemizer = new DefaultPhonemizer();
            Phonemize(project);
            Assert.Throws<ArgumentException>(() => VoiSonaState.FromPhrase(
                Assert.Single(((UVoicePart)project.parts[0]).renderPhrases), (VoiSonaSinger)project.tracks[0].Singer));
        }
        [Fact]
        public void KoreanPronunciationHintsReachNativeLyrics() {
            var project = KoreanFixture();
            ((UVoicePart)project.parts[0]).notes.First().lyric = "의 [n e]";
            Phonemize(project);
            var phrase = Assert.Single(((UVoicePart)project.parts[0]).renderPhrases);
            Assert.Equal("ね", VoiSonaState.Lyric(phrase.notes[0]));
            Assert.Equal("의 [n e]", ((UVoicePart)project.parts[0]).notes.First().lyric);
            Assert.Equal("+", VoiSonaState.Lyric(phrase.notes[2]));
        }
        static UProject PronunciationFixture(params string[] lyrics) {
            var project = KoreanFixture();
            var part = (UVoicePart)project.parts[0];
            part.notes.Clear();
            for (int i = 0; i < lyrics.Length; i++) {
                var note = project.CreateGridNote(178, i * 960, 960);
                note.lyric = lyrics[i]; part.notes.Add(note);
            }
            part.duration = lyrics.Length * 960;
            return project;
        }
        [Theory]
        [InlineData("앗", "あっ")]
        [InlineData("악", "あっ")]
        [InlineData("압", "あっ")]
        [InlineData("아 [a t]", "あっ")]
        public void StopCodaHasNoAddedVowel(string lyric, string expected) {
            var project = PronunciationFixture(lyric, "+"); Phonemize(project);
            var phrase = Assert.Single(((UVoicePart)project.parts[0]).renderPhrases);
            Assert.Equal(expected, VoiSonaState.Lyric(phrase.notes[0]));
            var merged = VoiSonaState.MergeExtensions(phrase.notes.Select(n =>
                new VoiSonaNote(n.positionMs, n.durationMs, n.adjustedTone, VoiSonaState.Lyric(n))).ToArray());
            Assert.Equal(expected, Assert.Single(merged).Lyric);
            Assert.Equal(2000, merged[0].DurationMs, 3);
        }
        [Theory]
        [InlineData("아", "하", "あ", "は")]
        [InlineData("안", "하", "あん", "は")]
        [InlineData("알", "하", "ある", "は")]
        [InlineData("악", "하", "あっ", "は")]
        [InlineData("아", "흐", "あ", "ふ")]
        [InlineData("아", "히 [ch i]", "あ", "ち")]
        public void SungHInitialSurvivesAndExplicitHintsStillWin(string first, string second, string a, string b) {
            var project = PronunciationFixture(first, "+", second); Phonemize(project);
            var phrase = Assert.Single(((UVoicePart)project.parts[0]).renderPhrases);
            Assert.Equal(a, VoiSonaState.Lyric(phrase.notes[0]));
            Assert.Equal(b, VoiSonaState.Lyric(phrase.notes[2]));
            Assert.Equal(new[] { first, "+", second }, ((UVoicePart)project.parts[0]).notes.Select(n => n.lyric));
        }
        [InstalledVoiSonaFact]
        public async Task StopClosureAndRestoredHProduceNativeAudio() {
            var project = PronunciationFixture("앗", "+", "하", "악", "흐", "압", "히");
            project.tracks[0].Singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Single(s => s.NoteLanguage == 1);
            Phonemize(project);
            var phrase = Assert.Single(((UVoicePart)project.parts[0]).renderPhrases);
            using var cancel = new CancellationTokenSource();
            var audio = await new VoiSonaRenderer().Render(phrase, new Progress(phrase.phones.Length), 0, cancel);
            Assert.True(audio.samples.All(float.IsFinite));
            Assert.True(Math.Sqrt(audio.samples.Average(x => (double)x*x)) > 0.005);
            string directory = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS");
            if (!string.IsNullOrEmpty(directory)) {
                System.IO.Directory.CreateDirectory(directory);
                using var writer = new NAudio.Wave.WaveFileWriter(System.IO.Path.Combine(directory, "batchim-h.wav"),
                    NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
                writer.WriteSamples(audio.samples, 0, audio.samples.Length);
                System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "batchim-h-lyrics.txt"),
                    phrase.notes.Select(n => n.lyric + " -> " + VoiSonaState.Lyric(n)));
            }
        }
        [InstalledVoiSonaFact]
        public async Task KoreanLyricsProduceAudibleNativeAudio() {
            var project = KoreanFixture();
            project.tracks[0].Singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Single(s => s.NoteLanguage == 1);
            Phonemize(project);
            var phrase = Assert.Single(((UVoicePart)project.parts[0]).renderPhrases);
            using var cancel = new CancellationTokenSource();
            var audio = await new VoiSonaRenderer().Render(phrase, new Progress(phrase.phones.Length), 0, cancel);
            Assert.True(audio.samples.Max(x => Math.Abs(x)) > 0.02f);
            Assert.True(Math.Sqrt(audio.samples.Average(x => (double)x*x)) > 0.005);
        }
    }
}
