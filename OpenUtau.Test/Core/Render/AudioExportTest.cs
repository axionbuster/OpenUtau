using System;
using System.IO;
using System.Linq;
using NAudio.Wave;
using OpenUtau.Core.Format;
using Xunit;

namespace OpenUtau.Core {
    public class AudioExportTest : IDisposable {
        readonly string directory = Path.Combine(Path.GetTempPath(), "OpenUtau-Flac-" + Guid.NewGuid());
        public AudioExportTest() => Directory.CreateDirectory(directory);
        public void Dispose() => Directory.Delete(directory, true);
        [Fact]
        public void CacheMatchesWavPcmAndCompresses() {
            var samples = Enumerable.Range(0, 286650).Select(i => (float)(.7 * Math.Sin(i * .03))).ToArray();
            string wav = Path.Combine(directory, "cache.wav"), flac = Path.Combine(directory, "cache.flac");
            Wave.WriteMono16Wav(wav, samples);
            Wave.WriteMonoCache(flac, samples);
            Assert.Equal(Wave.ReadMonoCache(wav), Wave.ReadMonoCache(flac));
            Assert.True(new FileInfo(flac).Length < new FileInfo(wav).Length);
            File.Delete(flac);
            var before = Wave.ReadMonoCache(wav);
            Wave.MigrateCache(flac);
            Assert.Equal(before, Wave.ReadMonoCache(flac));
            Assert.False(File.Exists(wav));
        }
        [Fact]
        public void FailedFlacWritePreservesDestination() {
            string path = Path.Combine(directory, "cache.flac");
            Wave.WriteMonoCache(path, new[] { .5f });
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => Wave.WriteMonoCache(path, new[] { float.NaN }));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        [Fact]
        public void HighResolutionCacheHasBoundedError() {
            var samples = Enumerable.Range(0, 286650).Select(i => (float)(.7 * Math.Sin(i * .03))).ToArray();
            string path = Path.Combine(directory, "cache.flac");
            Wave.WriteMonoCache(path, samples, 24);
            var decoded = Wave.ReadMonoCache(path);
            Assert.Equal(samples.Length, decoded.Length);
            double error = samples.Zip(decoded).Max(p => Math.Abs(p.First - p.Second));
            Assert.True(error <= 1f / 8388608, $"error={error}; first={string.Join(",", decoded.Take(8))}");
        }
        [Fact]
        public void StereoSeekAndPartialFinalFrameAreExact() {
            string wav = Path.Combine(directory, "stereo.wav"), flac = Path.Combine(directory, "stereo.flac");
            var samples = Enumerable.Range(0, 573300).Select(i => (float)(.6 * Math.Sin(i * .031))).ToArray();
            using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2))) writer.WriteSamples(samples, 0, samples.Length);
            using var source = Wave.OpenFile(wav);
            AudioExport.Write(flac, source.ToSampleProvider());
            using var decoded = Wave.OpenFile(flac);
            var all = Wave.GetSamples(decoded.ToSampleProvider());
            Assert.Equal(samples.Length, all.Length);
            decoded.Position = 12345 * decoded.WaveFormat.BlockAlign;
            var tail = Wave.GetSamples(decoded.ToSampleProvider());
            Assert.Equal(all.Skip(12345 * 2), tail);
            decoded.Position = 0;
            Assert.Equal(all, Wave.GetSamples(decoded.ToSampleProvider()));
        }

        [Fact]
        public async System.Threading.Tasks.Task ConcurrentFlacPublicationIsComplete() {
            string path = Path.Combine(directory, "parallel.flac");
            var samples = Enumerable.Repeat(.25f, 44101).ToArray();
            Wave.WriteMonoCache(path, samples);
            await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 4).Select(_ => System.Threading.Tasks.Task.Run(() => {
                for (int i = 0; i < 10; i++) {
                    Wave.WriteMonoCache(path, samples);
                    using var reader = Wave.OpenFile(path);
                    Assert.Equal(samples.Length, Wave.GetSamples(reader.ToSampleProvider()).Length);
                }
            })));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Theory]
        [InlineData(".wav")]
        [InlineData(".flac")]
        [InlineData(".m4a")]
        public void ExportWritesExpectedContainer(string extension) {
            if (extension == ".m4a" && !OperatingSystem.IsMacOS()) return;
            string source = Path.Combine(directory, "source.wav"), output = Path.Combine(directory, "export" + extension);
            Wave.WriteMono16Wav(source, Enumerable.Range(0, 44100).Select(i => (float)(.5 * Math.Sin(i * .03))).ToArray());
            using var reader = new WaveFileReader(source);
            AudioExport.Write(output, reader.ToSampleProvider());
            var bytes = File.ReadAllBytes(output);
            Assert.Equal(extension == ".wav" ? "RIFF" : extension == ".flac" ? "fLaC" : "ftyp",
                System.Text.Encoding.ASCII.GetString(bytes, extension == ".m4a" ? 4 : 0, 4));
            if (extension != ".m4a") Assert.Equal(44100, Wave.ReadMonoCache(output).Length);
        }
    }
}
