using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.VoiSona;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaExpressionsTest {
        [Fact]
        public void SettingsSurviveSaveCloneUndoAndInvalidatePhraseCache() {
            var singer = new VoiSonaSinger("ja_JP", "2.1.0", "/unused");
            var (project, before) = VoiSonaTest.NativePhrase(singer);
            var track = project.tracks[0];
            var settings = track.RendererSettings.Clone();
            settings.voisona = new VoiSonaSettings { Age = .5, Huskiness = 3, NativePitch = true,
                PitchAccuracy = -.3, VibratoAmplitude = 2, VibratoFrequency = .6 };
            var command = new TrackChangeRenderSettingCommand(project, track, settings);
            command.Execute();
            settings.voisona.Age = -.5;
            Assert.Equal(.5, track.RendererSettings.voisona.Age);
            var after = Assert.Single(RenderPhrase.FromPart(project, track, (UVoicePart)project.parts[0]));
            Assert.NotEqual(before.hash, after.hash);
            Assert.Equal(0, before.VoiSonaSettings.Age);
            project.BeforeSave();
            try {
                var restored = Format.Ustx31.Deserialize(Format.Ustx31.Serialize(project));
                Assert.Equal(.5, restored.tracks[0].RendererSettings.voisona.Age);
                Assert.Equal(.6, restored.tracks[0].RendererSettings.voisona.VibratoFrequency);
                Assert.True(restored.tracks[0].RendererSettings.voisona.NativePitch);
            } finally { project.AfterSave(); }
            command.Unexecute();
            Assert.Null(track.RendererSettings.voisona);
        }

        [Fact]
        public void CurvesFollowTempoAndChangeBothCaches() {
            var singer = new VoiSonaSinger("ja_JP", "2.1.0", "/unused");
            var (project, before) = VoiSonaTest.NativePhrase(singer);
            var part = (UVoicePart)project.parts[0];
            foreach (var descriptor in new VoiSonaRenderer().GetSuggestedExpressions(singer, project.tracks[0].RendererSettings)) {
                project.RegisterExpression(descriptor);
                var curve = new UCurve(descriptor);
                curve.xs.AddRange(new[] { 0, 960, 3840 });
                curve.ys.AddRange(new[] { 0, 100, 100 });
                part.curves.Add(curve);
            }
            var after = Assert.Single(RenderPhrase.FromPart(project, project.tracks[0], part));
            Assert.NotEqual(before.hash, after.hash);
            Assert.NotEqual(VoiSonaState.FromPhrase(before, singer).State, VoiSonaState.FromPhrase(after, singer).State);
            var age = after.curves.Single(c => c.Item1 == "alp").Item2;
            Assert.Equal(50, VoiSonaState.Sample(after, age, 1000), 4);
            Assert.Equal(100, VoiSonaState.Sample(after, age, 2000), 4);
        }

        [InstalledVoiSonaFact]
        public async Task NativeControlsChangeSynthesizedAudio() {
            string root = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS")
                ?? Path.Combine(Path.GetTempPath(), "voisona-expressions");
            Directory.CreateDirectory(root);
            var singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).First();
            float[]? baseline = null;
            foreach (var (name, settings, age, husky) in new (string, VoiSonaSettings, double?, double?)[] {
                ("default", new(), null, null),
                ("age", new() { Age = .6 }, null, null),
                ("huskiness", new() { Huskiness = 5 }, null, null),
                ("age-curve", new(), .6, null),
                ("huskiness-curve", new(), null, 5),
                ("vibrato", new() { VibratoAmplitude = 5 }, null, null),
                ("vibrato-rate", new() { VibratoAmplitude = 5, VibratoFrequency = .5 }, null, null),
                ("native-pitch", new() { NativePitch = true, PitchAccuracy = 1 }, null, null),
                ("pitch-accuracy", new() { NativePitch = true, PitchAccuracy = -1 }, null, null),
            }) {
                string dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
                var score = VoiSonaState.Build(singer, new[] { new VoiSonaNote(500, 2500, 69, "あ") },
                    3500, _ => 69, settings, age.HasValue ? _ => age.Value : null, husky.HasValue ? _ => husky.Value : null);
                var job = new VoiSonaJob { state = score.State, durationMs = score.DurationMs,
                    output = Path.Combine(dir, "audio.wav"), result = Path.Combine(dir, "result.json"),
                    cancel = Path.Combine(dir, "cancel"), noteWindows = new[] { new[] { 500.0, 3000.0 } } };
                await VoiSonaRenderer.RunHelper(VoiSonaRenderer.HelperPath, job, dir, CancellationToken.None);
                var audio = VoiSonaRenderer.TryReadAudio(job.output, 154350);
                Assert.NotNull(audio);
                if (baseline == null) baseline = audio;
                else {
                    double difference = Math.Sqrt(audio!.Zip(baseline, (a,b) => (double)(a-b)*(a-b)).Average());
                    await File.WriteAllTextAsync(Path.Combine(dir, "difference-rms.txt"), difference.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    Assert.True(difference > .0001, $"{name} did not affect synthesis: {difference}");
                }
            }
        }
    }
}
