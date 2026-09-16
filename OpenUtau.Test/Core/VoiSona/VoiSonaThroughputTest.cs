using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenUtau.Core.Render;
using OpenUtau.Core.VoiSona;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class VoiSonaThroughputTest {
        [InstalledVoiSonaFact]
        public void PlaybackMixdownAndStemExportPublishEveryTrack() {
            var singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Single(s => s.NoteLanguage == 1);
            var project = Format.Ustx.Create();
            project.tracks.Clear();
            for (int t = 0; t < 4; t++) {
                var track = new Ustx.UTrack { TrackNo = t, Singer = singer, Phonemizer = new DefaultPhonemizer() };
                track.RendererSettings.renderer = Renderers.VOISONA;
                project.tracks.Add(track);
                var part = new Ustx.UVoicePart { trackNo = t, duration = 1920 };
                for (int n = 0; n < 4; n++) {
                    var note = project.CreateNote(65 + t, n * 480, 480);
                    note.lyric = "あ"; note.tuning = 11;
                    part.notes.Add(note);
                }
                project.parts.Add(part);
            }
            VoiSonaLyricsTest.Phonemize(project);
            var previous = DocManager.Inst.TakeProjectForTest(project);
            var oldSink = DocManager.Inst.CommandSink;
            DocManager.Inst.CommandSink = _ => { };
            CancellationTokenSource cancellation = null!;
            string root = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS")
                ?? Path.Combine(Path.GetTempPath(), "voisona-throughput-artifacts");
            Directory.CreateDirectory(root);
            try {
                var planner = new MixPlanner();
                var engine = new RenderEngine(project);
                var routeWatch = Stopwatch.StartNew();
                var playback = engine.RenderMixdown(System.Threading.Tasks.TaskScheduler.Default,
                    ref cancellation, false, false, planner).Item1;
                Assert.True(SpinWait.SpinUntil(() => project.parts.All(p => planner.IsPartReady(p)), TimeSpan.FromMinutes(3)));
                double playbackSeconds = routeWatch.Elapsed.TotalSeconds;
                Assert.True(playback.IsReady(0, 44100));
                var buffer = new float[44100 * 6];
                Assert.True(playback.Mix(0, buffer, 0, buffer.Length) > 0);
                Assert.True(buffer.Any(s => Math.Abs(s) > .001f));
                foreach (var phrase in project.parts.OfType<Ustx.UVoicePart>().SelectMany(p => p.renderPhrases)) phrase.DeleteCacheFiles();
                routeWatch.Restart();
                var exportPlanner = new MixPlanner();
                var mix = engine.RenderMixdown(System.Threading.Tasks.TaskScheduler.Default,
                    ref cancellation, true, false, exportPlanner).Item1;
                NAudio.Wave.WaveFileWriter.CreateWaveFile16(Path.Combine(root, "mix.wav"),
                    new SignalChain.ExportAdapter(mix));
                double exportSeconds = routeWatch.Elapsed.TotalSeconds;
                File.WriteAllText(Path.Combine(root, "routes.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { playbackSeconds, exportSeconds }));
                var stems = engine.RenderTracks(System.Threading.Tasks.TaskScheduler.Default,
                    ref cancellation, new MixPlanner());
                Assert.Equal(4, stems.Count);
                for (int t = 0; t < stems.Count; t++) {
                    Assert.NotEmpty(stems[t].CurrentSlots);
                    Array.Clear(buffer);
                    Assert.True(stems[t].Mix(0, buffer, 0, buffer.Length) > 0);
                    Assert.All(buffer, s => Assert.True(float.IsFinite(s)));
                    Assert.True(buffer.Any(s => Math.Abs(s) > .001f));
                }
            } finally {
                cancellation?.Cancel(); cancellation?.Dispose();
                DocManager.Inst.CommandSink = oldSink;
                DocManager.Inst.TakeProjectForTest(previous);
            }
        }
        [InstalledVoiSonaFact]
        public void ColdParallelJobsAndDuplicateCacheRequestsProduceValidAudio() {
            var singer = VoiSonaSingerLoader.Discover(VoiSonaSingerLoader.VoiceRoot).Single(s => s.NoteLanguage == 1);
            var phrases = Enumerable.Range(0, 4).Select(i => {
                var (project, _) = VoiSonaTest.NativePhrase(singer);
                var part = (Ustx.UVoicePart)project.parts[0];
                foreach (var note in part.notes) note.tuning += 13 + i * 17;
                return Assert.Single(RenderPhrase.FromPart(project, project.tracks[0], part));
            }).ToArray();
            var rows = new List<object>();
            string root = Environment.GetEnvironmentVariable("OPENUTAU_VOISONA_ARTIFACTS")
                ?? Path.Combine(Path.GetTempPath(), "voisona-throughput-artifacts");
            Directory.CreateDirectory(root);
            using var cancel = new CancellationTokenSource();
            try {
                // Alternating repeated serial/export runs, plus the playback limit.
                foreach (int workers in new[] { 1, 4, 2, 4, 1 }) {
                    foreach (var phrase in phrases) phrase.DeleteCacheFiles();
                    var watch = Stopwatch.StartNew();
                    var outputs = BoundedRenderQueue.Run(phrases, workers,
                        (p, c) => p.renderer.Render(p, new Progress(p.phones.Length), 0, c), cancel.Token).ToArray();
                    double elapsed = watch.Elapsed.TotalSeconds;
                    Assert.Equal(4, outputs.Length);
                    foreach (var (phrase, output) in outputs) {
                        Assert.All(output.samples, s => Assert.True(float.IsFinite(s)));
                        Assert.True(output.samples.Any(s => Math.Abs(s) > .001f));
                        var cached = phrase.renderer.Render(phrase, new Progress(phrase.phones.Length), 0, cancel).GetAwaiter().GetResult();
                        Assert.Equal(output.samples, cached.samples);
                    }
                    rows.Add(new { workers, seconds = elapsed, frames = outputs.Select(o => o.result.samples.Length).ToArray() });
                    File.WriteAllText(Path.Combine(root, "timings.json"), Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
                }
                phrases[0].DeleteCacheFiles();
                var duplicates = BoundedRenderQueue.Run(new[] { phrases[0], phrases[0] }, 2,
                    (p, c) => p.renderer.Render(p, new Progress(p.phones.Length), 0, c), cancel.Token).ToArray();
                Assert.Equal(duplicates[0].result.samples, duplicates[1].result.samples);
            } finally {
                foreach (var phrase in phrases) phrase.DeleteCacheFiles();
            }
        }
    }
}
