using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.VoiSona;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaTest {
        static VoiSonaSinger Singer => new("ja_JP", "2.1.0", "/unused");
        [Fact]
        public void JuceStreamUsesIntegerLanguageAndUtf8() {
            var encoded = new VoiSonaTree("N", ("NoteLanguage", 1), ("Lyric", "あ")).Encode();
            Assert.Equal("4E0001024E6F74654C616E677561676500010501010000004C7972696300010505E381820000", Convert.ToHexString(encoded));
        }
        [Fact]
        public void NativePitchPreservesOne31TetStep() {
            Assert.Equal(440, Math.Exp(VoiSonaState.LogF0(69)), 8);
            Assert.Equal(440 * Math.Pow(2, 1.0 / 31), Math.Exp(VoiSonaState.LogF0(69 + 12.0 / 31)), 8);
        }
        [Fact]
        public void ExtensionsSustainLyricButDoNotBridgeRests() {
            var merged = VoiSonaState.MergeExtensions(new[] {
                new VoiSonaNote(500, 1000, 69, "あ"), new VoiSonaNote(1500, 500, 70, "+"), new VoiSonaNote(2000, 500, 71, "+~"),
            });
            Assert.Single(merged); Assert.Equal(2000, merged[0].DurationMs); Assert.Equal("あ", merged[0].Lyric);
            Assert.Throws<ArgumentException>(() => VoiSonaState.MergeExtensions(new[] { new VoiSonaNote(500, 500, 69, "+") }));
            Assert.Throws<ArgumentException>(() => VoiSonaState.MergeExtensions(new[] {
                new VoiSonaNote(500, 500, 69, "あ"), new VoiSonaNote(1500, 500, 70, "+~"),
            }));
        }
        [Fact]
        public void CacheIncludesStateEngineAndLength() {
            string a = VoiSonaRenderer.CacheKey(new byte[] {1, 2}, "voice1:plugin1", 5000);
            Assert.NotEqual(a, VoiSonaRenderer.CacheKey(new byte[] {1, 3}, "voice1:plugin1", 5000));
            Assert.NotEqual(a, VoiSonaRenderer.CacheKey(new byte[] {1, 2}, "voice2:plugin1", 5000));
            Assert.NotEqual(a, VoiSonaRenderer.CacheKey(new byte[] {1, 2}, "voice1:plugin2", 5000));
            Assert.NotEqual(a, VoiSonaRenderer.CacheKey(new byte[] {1, 2}, "voice1:plugin1", 6000));
        }
        [Fact]
        public void DiscoveryKeepsBothChisAVariantsAndUsesNewestVersion() {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try {
                foreach (var (language, version) in new[] { ("ja_JP", "2.1.0"), ("ja_JP", "2.0.0"), ("en_US", "2.2.0 Cross-Lingual") }) {
                    string name = $"nitech-jp_{language}_f008_svss";
                    string dir = Path.Combine(root, name, version); Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, name + ".tsnvoice"), "");
                }
                var singers = VoiSonaSingerLoader.Discover(root);
                Assert.Equal(2, singers.Count);
                Assert.Equal(2, singers.Select(s => s.Id).Distinct().Count());
                Assert.Equal("2.1.0", singers[0].Version); Assert.Equal(1, singers[0].NoteLanguage); Assert.Equal(2, singers[1].NoteLanguage);
            } finally { Directory.Delete(root, true); }
        }
        [Fact]
        public void SameVersionReplacementInvalidatesInMemoryPhraseCache() {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
            try {
                string model = Path.Combine(dir, "nitech-jp_ja_JP_f008_svss.tsnvoice");
                File.WriteAllText(model, "old fixture");
                var first = new VoiSonaSinger("ja_JP", "2.1.0", dir);
                var (_, before) = NativePhrase(first);
                File.WriteAllText(model, "replacement fixture with the same version label");
                var refreshed = new VoiSonaSinger("ja_JP", "2.1.0", dir);
                var (_, after) = NativePhrase(refreshed);
                Assert.NotEqual(first.CacheStamp, refreshed.CacheStamp);
                Assert.NotEqual(before.hash, after.hash);
            } finally { Directory.Delete(dir, true); }
        }
        [Fact]
        public void InvalidOrSilentAudioCannotBecomeCacheHit() {
            string path = Path.GetTempFileName();
            try {
                File.WriteAllText(path, "truncated"); Assert.Null(VoiSonaRenderer.TryReadAudio(path, 100));
                using (var writer = new NAudio.Wave.WaveFileWriter(path, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))) {
                    writer.WriteSamples(new float[200], 0, 200);
                }
                Assert.Null(VoiSonaRenderer.TryReadAudio(path, 100));
            } finally { File.Delete(path); }
        }
        [Fact]
        public void RejectsMalformedNotesAndNonfinitePitch() {
            Assert.Throws<ArgumentException>(() => VoiSonaState.Build(Singer, new[] { new VoiSonaNote(0, -1, 69, "あ") }, 1000, _ => 69));
            Assert.Throws<ArgumentException>(() => VoiSonaState.Build(Singer, new[] { new VoiSonaNote(0, 500, 69, "あ") }, 1000, _ => double.NaN));
        }
        [Fact]
        public async Task CanceledJobDoesNotLaunchHelper() {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VoiSonaRenderer.RunHelper("not-an-executable", new(), "/unused", cancel.Token));
        }
        [MacFact]
        public async Task HelperRunsInsideItsIsolatedJobDirectory() {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
            try {
                string script = Path.Combine(dir, "fake-host");
                await File.WriteAllTextAsync(script, "#!/bin/sh\npwd > cwd.txt\nprintf '%s' '{\"protocolVersion\":1,\"ok\":true,\"frames\":44100,\"sampleRate\":44100}' > result.json\n");
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var job = new VoiSonaJob { durationMs = 1000, cancel = Path.Combine(dir, "cancel"), result = Path.Combine(dir, "result.json") };
                await VoiSonaRenderer.RunHelper(script, job, dir, CancellationToken.None);
                Assert.True(File.Exists(Path.Combine(dir, "cwd.txt")));
                Assert.Equal(Path.GetFileName(dir), Path.GetFileName((await File.ReadAllTextAsync(Path.Combine(dir, "cwd.txt"))).Trim()));
            } finally { Directory.Delete(dir, true); }
        }
        [MacFact]
        public async Task SilentSuccessfulProcessIsNotASuccessfulRender() {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
            try {
                string script = Path.Combine(dir, "fake-host");
                await File.WriteAllTextAsync(script, "#!/bin/sh\nexit 0\n");
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var job = new VoiSonaJob { cancel = Path.Combine(dir, "cancel"), result = Path.Combine(dir, "result"), output = Path.Combine(dir, "audio.wav") };
                await Assert.ThrowsAsync<InvalidOperationException>(() => VoiSonaRenderer.RunHelper(script, job, dir, CancellationToken.None));
                Assert.False(File.Exists(job.output));
            } finally { Directory.Delete(dir, true); }
        }
        [MacFact]
        public async Task HelperTimeoutAndCancellationAreBounded() {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
            try {
                string script = Path.Combine(dir, "fake-host");
                await File.WriteAllTextAsync(script, "#!/bin/sh\nexec /bin/sleep 30\n");
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var job = new VoiSonaJob { cancel = Path.Combine(dir, "cancel"), result = Path.Combine(dir, "result") };
                await Assert.ThrowsAsync<TimeoutException>(() => VoiSonaRenderer.RunHelper(script, job, dir, CancellationToken.None, timeout: TimeSpan.FromMilliseconds(100)));
                using var cancel = new CancellationTokenSource(100);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VoiSonaRenderer.RunHelper(script, job, dir, cancel.Token));
            } finally { Directory.Delete(dir, true); }
        }
        [MacFact]
        public async Task PersistentHostServesBackToBackGenerationsInOneProcess() {
            string root = Path.Combine(Path.GetTempPath(), "voisona-pool-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string script = await WriteFakeHost(root, """
                    #!/bin/sh
                    base=$(/usr/bin/dirname "$0")
                    [ "$1" = "--server" ] || exit 91
                    printf 'READY 2\n'
                    while IFS= read -r request; do
                      json=$(/bin/cat "$request")
                      generation=$(printf '%s' "$json" | /usr/bin/sed -E 's/.*"generation":"([^"]+)".*/\1/')
                      result=$(printf '%s' "$json" | /usr/bin/sed -E 's/.*"result":"([^"]+)".*/\1/')
                      printf '%s\n' "$$" >> "$base/pids"
                      printf '{"protocolVersion":2,"generation":"%s","ok":true,"frames":44100,"sampleRate":44100}' "$generation" > "$result"
                      printf 'DONE %s\n' "$generation"
                    done
                    """);
                using var pool = new VoiSonaHostPool(script, idleTimeout: TimeSpan.FromSeconds(30), lowPriority: false);
                foreach (string generation in new[] { "first", "second" }) {
                    string dir = Path.Combine(root, generation); Directory.CreateDirectory(dir);
                    var job = Protocol2Job(dir, generation);
                    await pool.RunAsync(job, dir, TestContext.Current.CancellationToken, timeout: TimeSpan.FromSeconds(2));
                }
                string[] pids = await File.ReadAllLinesAsync(Path.Combine(root, "pids"), TestContext.Current.CancellationToken);
                Assert.Equal(2, pids.Length);
                Assert.Equal(pids[0], pids[1]);
            } finally { Directory.Delete(root, true); }
        }
        [MacFact]
        public async Task DeadPersistentHostFallsBackToOneShotHelper() {
            string root = Path.Combine(Path.GetTempPath(), "voisona-fallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string script = await WriteFakeHost(root, """
                    #!/bin/sh
                    base=$(/usr/bin/dirname "$0")
                    if [ "$1" = "--server" ]; then
                      printf 'READY 2\n'
                      IFS= read -r request
                      exit 17
                    fi
                    json=$(/bin/cat "$1")
                    result=$(printf '%s' "$json" | /usr/bin/sed -E 's/.*"result":"([^"]+)".*/\1/')
                    printf '{"protocolVersion":1,"ok":true,"frames":44100,"sampleRate":44100}' > "$result"
                    : > "$base/oneshot-ran"
                    """);
                using var pool = new VoiSonaHostPool(script, lowPriority: false);
                string dir = Path.Combine(root, "job"); Directory.CreateDirectory(dir);
                var job = Protocol2Job(dir, "will-be-replaced");
                await VoiSonaRenderer.RunPlaybackHelper(pool, script, job, dir,
                    TestContext.Current.CancellationToken, timeout: TimeSpan.FromSeconds(2));
                Assert.True(File.Exists(Path.Combine(root, "oneshot-ran")));
                Assert.Equal(1, job.protocolVersion);
                Assert.Equal("", job.generation);
            } finally { Directory.Delete(root, true); }
        }
        [MacFact]
        public async Task CanceledPersistentJobIsKilledAndReplaced() {
            string root = Path.Combine(Path.GetTempPath(), "voisona-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string script = await WriteFakeHost(root, """
                    #!/bin/sh
                    base=$(/usr/bin/dirname "$0")
                    [ "$1" = "--server" ] || exit 91
                    printf 'READY 2\n'
                    while IFS= read -r request; do
                      json=$(/bin/cat "$request")
                      generation=$(printf '%s' "$json" | /usr/bin/sed -E 's/.*"generation":"([^"]+)".*/\1/')
                      result=$(printf '%s' "$json" | /usr/bin/sed -E 's/.*"result":"([^"]+)".*/\1/')
                      printf '%s\n' "$$" >> "$base/pids"
                      if [ ! -e "$base/blocked-once" ]; then
                        : > "$base/blocked-once"
                        /bin/sleep 30
                      else
                        printf '{"protocolVersion":2,"generation":"%s","ok":true,"frames":44100,"sampleRate":44100}' "$generation" > "$result"
                        printf 'DONE %s\n' "$generation"
                      fi
                    done
                    """);
                using var pool = new VoiSonaHostPool(script, idleTimeout: TimeSpan.FromSeconds(30), lowPriority: false);
                string firstDir = Path.Combine(root, "first"); Directory.CreateDirectory(firstDir);
                using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300))) {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.RunAsync(
                        Protocol2Job(firstDir, "canceled"), firstDir, cancel.Token, timeout: TimeSpan.FromSeconds(5)));
                }
                string secondDir = Path.Combine(root, "second"); Directory.CreateDirectory(secondDir);
                await pool.RunAsync(Protocol2Job(secondDir, "fresh"), secondDir,
                    TestContext.Current.CancellationToken, timeout: TimeSpan.FromSeconds(2));
                string[] pids = await File.ReadAllLinesAsync(Path.Combine(root, "pids"), TestContext.Current.CancellationToken);
                Assert.Equal(2, pids.Length);
                Assert.NotEqual(pids[0], pids[1]);
            } finally { Directory.Delete(root, true); }
        }
        static VoiSonaJob Protocol2Job(string directory, string generation) => new() {
            protocolVersion = 2,
            generation = generation,
            durationMs = 1000,
            cancel = Path.Combine(directory, "cancel"),
            result = Path.Combine(directory, "result.json"),
            output = Path.Combine(directory, "audio.wav"),
        };
        static async Task<string> WriteFakeHost(string directory, string contents) {
            string script = Path.Combine(directory, "fake-host");
            await File.WriteAllTextAsync(script, contents, TestContext.Current.CancellationToken);
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return script;
        }
        internal static (UProject, RenderPhrase) NativePhrase(VoiSonaSinger singer) {
            var project = Format.Ustx.Create(); project.Is31Edo = true;
            project.tempos.Clear(); project.tempos.Add(new UTempo(0, 120)); project.tempos.Add(new UTempo(1440, 80));
            project.timeAxis.BuildSegments(project);
            project.tracks.Clear();
            var track = new UTrack { TrackNo = 0, Singer = singer };
            track.RendererSettings.renderer = Renderers.VOISONA;
            track.RendererSettings.Validate(track); project.tracks.Add(track);
            var part = new UVoicePart { trackNo = 0, position = 480 }; project.parts.Add(part);
            UNote? previous = null;
            foreach (int i in Enumerable.Range(0, 4)) {
                var note = project.CreateGridNote(178 + i, i * 960, 960);
                note.lyric = singer.NoteLanguage == 1 ? "あ" : "ah";
                note.tuning = 7; note.ExtendedDuration = note.duration;
                note.Prev = previous; if (previous != null) previous.Next = note;
                part.notes.Add(note); previous = note;
                var phone = new UPhoneme { position = note.position, phoneme = note.lyric, Parent = note };
                part.phonemes.Add(phone);
            }
            foreach (var phone in part.phonemes) phone.Validate(new ValidateOptions(), project, track, part, phone.Parent);
            part.duration = part.notes.Last().End;
            return (project, Assert.Single(RenderPhrase.FromPart(project, track, part)));
        }
        [Fact]
        public void PhraseConversionUsesNativePitchAndTempoMappedDuration() {
            var (project, phrase) = NativePhrase(Singer);
            Assert.Equal(178 * 12.0 / 31 + 0.07, phrase.notes[0].adjustedTone, 4);
            Assert.Equal(440 * Math.Pow(2,
                    (phrase.notes[0].adjustedTone - Edo31.A4Step * Edo31.StepTone) / 12),
                phrase.ToneToFrequency(phrase.notes[0].adjustedTone), 8);
            Assert.Equal(5500, phrase.durationMs, 5);
            var score = VoiSonaState.FromPhrase(phrase, Singer);
            Assert.Equal(6500, score.DurationMs, 5);

            project.PitchReference31 = new Edo31PitchReference {
                Mode = Edo31PitchReferenceMode.A4Frequency,
                A4Frequency = 442,
            };
            var reanchored = Assert.Single(RenderPhrase.FromPart(
                project, project.tracks[0], (UVoicePart)project.parts[0]));
            Assert.Equal(442 * Math.Pow(2,
                    (reanchored.notes[0].adjustedTone - Edo31.A4Step * Edo31.StepTone) / 12),
                reanchored.ToneToFrequency(reanchored.notes[0].adjustedTone), 8);
            Assert.NotEqual(phrase.hash, reanchored.hash);
            Assert.NotEqual(score.State, VoiSonaState.FromPhrase(reanchored, Singer).State);

            var (_, updated) = NativePhrase(new VoiSonaSinger("ja_JP", "2.2.0", "/unused"));
            Assert.NotEqual(phrase.hash, updated.hash);
        }
        [InstalledVoiSonaFact]
        public async Task StalledTeardownPreservesCommittedSuccessAndRejectsCanceledAudio() {
            string dir = Path.Combine(Path.GetTempPath(), "voisona-teardown-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try {
                string wrapper = Path.Combine(dir, "stall-host");
                string quotedHelper = "'" + VoiSonaRenderer.HelperPath.Replace("'", "'\"'\"'") + "'";
                await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nexec " + quotedHelper + " --test-teardown-stall \"$1\"\n");
                File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).First();
                var score = VoiSonaState.Build(singer, new[] {new VoiSonaNote(500, 1000, 69, "あ")}, 2000, _ => 69);
                var job = new VoiSonaJob {state = score.State, durationMs = 2000,
                    output = Path.Combine(dir, "audio.wav"), result = Path.Combine(dir, "result.json"), cancel = Path.Combine(dir, "cancel"),
                    noteWindows = new[] {new[] {500.0, 1500.0}}};
                await VoiSonaRenderer.RunHelper(wrapper, job, dir, CancellationToken.None, timeout: TimeSpan.FromSeconds(45));
                Assert.NotNull(VoiSonaRenderer.TryReadAudio(job.output, 88200));
                File.Delete(job.output); File.Delete(job.result); await File.WriteAllTextAsync(job.cancel, "cancel");
                await Assert.ThrowsAsync<InvalidOperationException>(() => VoiSonaRenderer.RunHelper(wrapper, job, dir, CancellationToken.None, timeout: TimeSpan.FromSeconds(20)));
                Assert.False(File.Exists(job.output));
                Assert.False(JObject.Parse(await File.ReadAllTextAsync(job.result)).Value<bool>("ok"));
            } finally { Directory.Delete(dir, true); }
        }
        [InstalledVoiSonaFact]
        public async Task ActualNativePhraseRendersAndCachesWithTempoChange() {
            string artifacts = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS") ?? Path.Combine(Path.GetTempPath(), "voisona-artifacts");
            Directory.CreateDirectory(artifacts);
            foreach (var singer in VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot)) {
                var (project, phrase) = NativePhrase(singer);
                project.BeforeSave();
                try { await File.WriteAllTextAsync(Path.Combine(artifacts, singer.Language + ".ustx31"), Format.Ustx31.Serialize(project)); }
                finally { project.AfterSave(); }
                using var cancel = new CancellationTokenSource();
                var renderer = new VoiSonaRenderer();
                var result = await renderer.Render(phrase, new Progress(phrase.phones.Length), 0, cancel);
                Assert.NotNull(result.samples); Assert.Equal(286650, result.samples.Length);
                var cached = await renderer.Render(phrase, new Progress(phrase.phones.Length), 0, cancel);
                Assert.Equal(result.samples, cached.samples);
                using var writer = new NAudio.Wave.WaveFileWriter(Path.Combine(artifacts, singer.Language + ".wav"), NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
                writer.WriteSamples(result.samples, 0, result.samples.Length);
                var score = VoiSonaState.FromPhrase(phrase, singer);
                var job = new VoiSonaJob { state = score.State, durationMs = score.DurationMs,
                    output = Path.Combine(artifacts, singer.Language + "-installed.wav"), result = Path.Combine(artifacts, singer.Language + "-installed-result.json"),
                    cancel = Path.Combine(artifacts, "cancel"), noteWindows = phrase.notes.Select(n => new[] {n.positionMs - phrase.positionMs + 500, n.endMs - phrase.positionMs + 500}).ToArray() };
                await File.WriteAllTextAsync(Path.Combine(artifacts, singer.Language + "-request.json"), Newtonsoft.Json.JsonConvert.SerializeObject(job));
            }
        }
        [InstalledVoiSonaFact]
        public async Task InstalledChisAVoicesRenderFreshGeneratedStateBeyondSixSeconds() {
            foreach (var singer in VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot)) {
                string dir = Path.Combine(Path.GetTempPath(), "voisona-integration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
                try {
                    string lyric = singer.NoteLanguage == 1 ? "あ" : "ah";
                    var notes = new[] { new VoiSonaNote(500, 1000, 69, lyric), new VoiSonaNote(3000, 1000, 69, lyric), new VoiSonaNote(7000, 1000, 69, lyric) };
                    var score = VoiSonaState.Build(singer, notes, 9000, _ => 69 + 12.0 / 31);
                    var job = new VoiSonaJob { state = score.State, durationMs = score.DurationMs,
                        output = Path.Combine(dir, "audio.wav"), result = Path.Combine(dir, "result.json"), cancel = Path.Combine(dir, "cancel"),
                        noteWindows = notes.Select(n => new[] {n.StartMs, n.StartMs + n.DurationMs}).ToArray() };
                    await VoiSonaRenderer.RunHelper(VoiSonaRenderer.HelperPath, job, dir, CancellationToken.None);
                    var samples = VoiSonaRenderer.TryReadAudio(job.output, 396900);
                    Assert.NotNull(samples);
                    Assert.True(samples!.Skip(7 * 44100).Any(v => Math.Abs(v) > 0.00001));
                } finally { Directory.Delete(dir, true); }
            }
            Assert.Equal(2, VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Count);
        }
    }
    public sealed class MacFactAttribute : FactAttribute {
        public MacFactAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "Requires macOS process lifecycle."; }
    }
    public sealed class InstalledVoiSonaFactAttribute : FactAttribute {
        public InstalledVoiSonaFactAttribute() {
            if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("OPENUTAU_TEST_VOISONA") != "1")
                Skip = "Opt in with OPENUTAU_TEST_VOISONA=1 after installing and signing into VoiSona Song.";
        }
    }
}
