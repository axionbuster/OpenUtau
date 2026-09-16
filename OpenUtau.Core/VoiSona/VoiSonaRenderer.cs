using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.VoiSona {
    public sealed class VoiSonaRenderer : IRenderer {
        static readonly SemaphoreSlim Gate = new(1);
        public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "openutau-voisona-host");
        public USingerType SingerType => USingerType.VoiSona;
        public bool SupportsRenderPitch => false;
        public bool SupportsExpression(UExpressionDescriptor descriptor) => descriptor.abbr is Format.Ustx.DYN or Format.Ustx.PITD;
        public override string ToString() => Renderers.VOISONA;
        public (double HeadMs, double TailMs) PhrasePadding(USinger singer, IEnumerable<UPhoneme> phonemes)
            => (VoiSonaState.HeadMs, VoiSonaState.TailMs);
        public RenderResult Layout(RenderPhrase phrase) => new() {
            leadingMs = VoiSonaState.HeadMs,
            positionMs = phrase.positionMs,
            estimatedLengthMs = phrase.durationMs + VoiSonaState.HeadMs + VoiSonaState.TailMs,
        };
        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null!;
        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings settings) => Array.Empty<UExpressionDescriptor>();

        public async Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null) {
            var token = cancellation.Token;
            await Gate.WaitAsync(token);
            try {
                token.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("VoiSona rendering requires macOS.");
                if (phrase.singer is not VoiSonaSinger singer) throw new InvalidOperationException("Select a VoiSona singer first.");
                if (VoiSonaSingerLoader.PluginPath == null) throw new InvalidOperationException("The VoiSona Song Audio Unit is missing. Reinstall its Audio Unit plugin.");
                if (!File.Exists(Path.Combine(singer.Location, singer.VoiceFileName)))
                    throw new InvalidOperationException($"{singer.Name} {singer.Version} is missing. Open Tools → VoiSona → Sign in / Manage voices, install the voice, then Refresh voices.");
                if (!File.Exists(HelperPath)) throw new InvalidOperationException("The VoiSona rendering helper is missing. Reinstall this OpenUtau build.");
                string info = $"Track {trackNo + 1}: VoiSona — preparing {singer.Name}";
                progress.Complete(0, info);
                var score = VoiSonaState.FromPhrase(phrase, singer);
                string key = CacheKey(score.State, EngineIdentity(singer), score.DurationMs);
                string cache = Path.Combine(PathManager.Inst.CachePath, $"voisona-{key}.wav");
                Directory.CreateDirectory(PathManager.Inst.CachePath);
                phrase.AddCacheFile(cache);
                int frames = (int)Math.Ceiling(score.DurationMs * 44.1);
                float[]? samples = TryReadAudio(cache, frames);
                if (samples == null) {
                    if (File.Exists(cache)) File.Delete(cache);
                    string directory = Path.Combine(PathManager.Inst.CachePath, "voisona-job-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);
                    try {
                        var job = new VoiSonaJob {
                            state = score.State, durationMs = score.DurationMs,
                            output = Path.Combine(directory, "audio.wav"), result = Path.Combine(directory, "result.json"),
                            cancel = Path.Combine(directory, "cancel"),
                            noteWindows = phrase.notes.Where(n => n.endMs > phrase.positionMs && n.positionMs < phrase.endMs)
                                .Select(n => new[] { n.positionMs - phrase.positionMs + VoiSonaState.HeadMs,
                                    n.endMs - phrase.positionMs + VoiSonaState.HeadMs }).ToArray(),
                        };
                        await RunHelper(HelperPath, job, directory, token, message => progress.Complete(0, $"Track {trackNo + 1}: {message}"));
                        samples = TryReadAudio(job.output, frames) ?? throw new InvalidOperationException("VoiSona returned invalid or silent audio. No audio was cached.");
                        token.ThrowIfCancellationRequested();
                        File.Move(job.output, cache, true);
                    } finally {
                        try { Directory.Delete(directory, true); } catch (IOException) { }
                    }
                }
                token.ThrowIfCancellationRequested();
                var result = Layout(phrase);
                result.samples = samples;
                Renderers.ApplyDynamics(phrase, result);
                progress.Complete(phrase.phones.Length, $"Track {trackNo + 1}: VoiSona");
                return result;
            } finally { Gate.Release(); }
        }
        internal static string CacheKey(byte[] state, string identity, double durationMs) {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                writer.Write(VoiSonaState.Schema); writer.Write(identity); writer.Write(durationMs); writer.Write(state);
            }
            return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        }
        static string EngineIdentity(VoiSonaSinger singer) {
            string plugin = VoiSonaSingerLoader.PluginPath!;
            var voice = new FileInfo(Path.Combine(singer.Location, singer.VoiceFileName));
            var binary = new FileInfo(Path.Combine(plugin, "Contents/MacOS/VoiSona Song"));
            // Hash the helper and public version manifest; inspect model file metadata only.
            return singer.Id + ":" + singer.Version + ":" + voice.Length + ":" + voice.LastWriteTimeUtc.Ticks
                + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(HelperPath)))
                + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(plugin, "Contents/Info.plist"))))
                + ":" + (binary.Exists ? binary.Length + ":" + binary.LastWriteTimeUtc.Ticks : "missing");
        }
        internal static float[]? TryReadAudio(string path, int frames) {
            if (!File.Exists(path)) return null;
            try {
                using var reader = new WaveFileReader(path);
                if (reader.WaveFormat.SampleRate != 44100 || reader.SampleCount != frames || reader.WaveFormat.Channels != 2) return null;
                var samples = Format.Wave.GetSamples(reader.ToSampleProvider().ToMono(0.5f, 0.5f));
                if (samples.Length != frames || samples.Any(v => !float.IsFinite(v)) || !samples.Any(v => Math.Abs(v) > 0.000001f)) return null;
                return samples;
            } catch (Exception e) when (e is IOException or FormatException or ArgumentException) { return null; }
        }
        internal static async Task RunHelper(string helper, VoiSonaJob job, string directory, CancellationToken token,
                Action<string>? report = null, TimeSpan? timeout = null) {
            token.ThrowIfCancellationRequested();
            string request = Path.Combine(directory, "request.json");
            await File.WriteAllTextAsync(request, JsonConvert.SerializeObject(job), token);
            using var process = new Process { StartInfo = new ProcessStartInfo(helper) {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            }};
            process.StartInfo.ArgumentList.Add(request);
            if (!process.Start()) throw new InvalidOperationException("Could not start the VoiSona renderer.");
            var outputTask = Task.Run(async () => {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null) {
                    if (line.StartsWith("Preparing VoiSona", StringComparison.Ordinal)) report?.Invoke(line);
                }
            });
            // Drain stderr without retaining unbounded plugin diagnostics.
            var errorTask = Task.Run(async () => {
                var lines = new Queue<string>(); string? line;
                while ((line = await process.StandardError.ReadLineAsync()) != null) {
                    lines.Enqueue(line.Length > 2048 ? line[..2048] : line);
                    while (lines.Count > 12) lines.Dequeue();
                }
                return string.Join("\n", lines);
            });
            using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(Math.Max(150, job.durationMs / 1000 * 2 + 30) + 10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException) {
                // Request normal graph teardown first; a hung plugin gets a bounded process-tree kill.
                await File.WriteAllTextAsync(job.cancel, "cancel");
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await process.WaitForExitAsync(grace.Token); }
                catch (OperationCanceledException) {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                await Task.WhenAll(outputTask, errorTask);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("VoiSona rendering timed out. Open Tools → VoiSona → Sign in / Manage voices and check that the selected voice is installed and licensed.");
            }
            await outputTask;
            string stderr = await errorTask;
            JObject? response = File.Exists(job.result) ? JObject.Parse(await File.ReadAllTextAsync(job.result, token)) : null;
            if (process.ExitCode != 0 || response?.Value<bool>("ok") != true || response.Value<int>("protocolVersion") != 1
                    || response.Value<int>("frames") != (int)Math.Ceiling(job.durationMs * 44.1) || response.Value<int>("sampleRate") != 44100) {
                string detail = response?.Value<string>("error") ?? $"VoiSona helper exited with code {process.ExitCode}. {stderr}";
                throw new InvalidOperationException(detail);
            }
        }
        public static Process OpenSetup() {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("VoiSona requires macOS.");
            if (!File.Exists(HelperPath)) throw new FileNotFoundException("VoiSona helper is missing. Reinstall OpenUtau.", HelperPath);
            var start = new ProcessStartInfo(HelperPath) { UseShellExecute = false };
            start.ArgumentList.Add("--setup");
            return Process.Start(start) ?? throw new InvalidOperationException("Could not open VoiSona setup.");
        }
    }
    internal sealed class VoiSonaJob {
        public int protocolVersion = 1;
        public byte[] state = Array.Empty<byte>();
        public double durationMs;
        public string output = "";
        public string result = "";
        public string cancel = "";
        public double[][] noteWindows = Array.Empty<double[]>();
    }
}
