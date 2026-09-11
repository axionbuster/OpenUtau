using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core.Format;
using Xunit;

namespace OpenUtau.Core {
    public class WaveCacheTest : IDisposable {
        readonly string directory = Path.Combine(Path.GetTempPath(), "OpenUtau-WaveCache-" + Guid.NewGuid());
        string CachePath => Path.Combine(directory, "phrase.wav");

        public WaveCacheTest() => Directory.CreateDirectory(directory);
        public void Dispose() => Directory.Delete(directory, true);

        [Fact]
        public void InvalidCacheCanBeRegenerated() {
            Assert.Null(Wave.ReadMonoCache(CachePath));
            foreach (var bytes in new[] { Array.Empty<byte>(), new byte[] { 1, 2, 3 }, "RIFF\0\0\0\0WAVE"u8.ToArray() }) {
                File.WriteAllBytes(CachePath, bytes);
                Assert.Null(Wave.ReadMonoCache(CachePath));
                Wave.WriteMono16Wav(CachePath, new float[] { 0.5f, -0.5f });
                var samples = Wave.ReadMonoCache(CachePath);
                Assert.Equal(2, samples.Length);
                Assert.InRange(samples[0], 0.49f, 0.51f);
                Assert.InRange(samples[1], -0.51f, -0.49f);
            }
        }

        [Fact]
        public void FailedWritePreservesExistingCacheAndCleansTemporaryFile() {
            Wave.WriteMono16Wav(CachePath, new float[] { 0.5f });
            var before = File.ReadAllBytes(CachePath);
            Assert.Throws<NullReferenceException>(() => Wave.WriteMono16Wav(CachePath, null));
            Assert.Equal(before, File.ReadAllBytes(CachePath));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task ConcurrentWritersPublishOnlyCompleteAudio() {
            var samples = Enumerable.Repeat(0.25f, 44100).ToArray();
            Wave.WriteMono16Wav(CachePath, samples);
            var writers = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
                for (int i = 0; i < 30; ++i) {
                    Wave.WriteMono16Wav(CachePath, samples);
                }
            })));
            // Read the actual published file, without the cache-miss fallback hiding errors.
            for (int i = 0; i < 120; ++i) {
                using var stream = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new WaveFileReader(stream);
                Assert.Equal(44100 * 2, reader.Length);
                var decoded = Wave.GetSamples(reader.ToSampleProvider());
                Assert.Equal(44100, decoded.Length);
                Assert.All(decoded, value => Assert.InRange(value, 0.249f, 0.251f));
            }
            await writers;
            Assert.Single(Directory.GetFiles(directory));
        }
    }
}
