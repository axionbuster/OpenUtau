using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace OpenUtau.Core.Format {
    /// <summary>Streaming audio export; completed files replace the destination atomically.</summary>
    public static class AudioExport {
        public static void Write(string path, ISampleProvider source) {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".wav" or ".flac" or ".m4a"))
                throw new ArgumentException("Choose a WAV, FLAC, or M4A filename.", nameof(path));
            if (extension == ".m4a" && !OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("M4A export requires macOS.");
            string temp = path + "." + Guid.NewGuid().ToString("N") + extension;
            string wav = temp + ".wav";
            try {
                if (extension == ".wav") WaveFileWriter.CreateWaveFile16(temp, source);
                else if (extension == ".flac") WriteFlac(temp, source);
                else {
                    WaveFileWriter.CreateWaveFile16(wav, source);
                    using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/afconvert") {
                        UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true,
                    }};
                    foreach (string arg in new[] { "-f", "m4af", "-d", "aac", "-b", "256000", wav, temp })
                        process.StartInfo.ArgumentList.Add(arg);
                    process.Start();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000)) {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                        throw new IOException("M4A encoding timed out.");
                    }
                    if (process.ExitCode != 0) throw new IOException("M4A encoding failed: " + error.GetAwaiter().GetResult());
                }
                File.Move(temp, path, overwrite: true);
            } finally {
                File.Delete(temp);
                File.Delete(wav);
            }
        }

        // The native runtime packages supply libFLAC and libogg on each platform.
        static AudioExport() {
            if (OperatingSystem.IsMacOS()) {
                // Load the packaged dependency by its unversioned NuGet filename.
                NativeLibrary.Load("libogg.dylib", typeof(AudioExport).Assembly, null);
            } else if (OperatingSystem.IsLinux()) {
                NativeLibrary.Load("libogg.so", typeof(AudioExport).Assembly, null);
            }
        }
        internal static void EnsureNativeLoaded() { }
        const string Flac = "FLAC";
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FLAC__stream_encoder_new();
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern void FLAC__stream_encoder_delete(IntPtr encoder);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_set_channels(IntPtr encoder, uint value);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_set_bits_per_sample(IntPtr encoder, uint value);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_set_sample_rate(IntPtr encoder, uint value);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_set_compression_level(IntPtr encoder, uint value);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_init_stream(IntPtr encoder,
            WriteCallback write, SeekCallback seek, TellCallback tell, IntPtr metadata, IntPtr client);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_process_interleaved(IntPtr encoder, int[] samples, uint frames);
        [DllImport(Flac, CallingConvention = CallingConvention.Cdecl)] static extern int FLAC__stream_encoder_finish(IntPtr encoder);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int WriteCallback(IntPtr encoder, IntPtr buffer, UIntPtr bytes, uint samples, uint frame, IntPtr client);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int SeekCallback(IntPtr encoder, ulong offset, IntPtr client);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int TellCallback(IntPtr encoder, out ulong offset, IntPtr client);

        internal static void WriteFlac(string path, ISampleProvider source, int bits = 16, bool preservePcm = false) {
            if (bits is not (16 or 24)) throw new ArgumentOutOfRangeException(nameof(bits));
            using var stream = File.Create(path);
            Exception? failure = null;
            WriteCallback write = (_, buffer, bytes, _, _, _) => {
                try {
                    unsafe { stream.Write(new ReadOnlySpan<byte>(buffer.ToPointer(), checked((int)bytes.ToUInt64()))); }
                    return 0;
                } catch (Exception e) { failure = e; return 1; }
            };
            SeekCallback seek = (_, offset, _) => {
                try { stream.Position = checked((long)offset); return 0; }
                catch (Exception e) { failure = e; return 1; }
            };
            TellCallback tell = (IntPtr _, out ulong offset, IntPtr _) => {
                offset = (ulong)stream.Position; return 0;
            };
            IntPtr encoder = FLAC__stream_encoder_new();
            if (encoder == IntPtr.Zero) throw new IOException("Could not allocate FLAC encoder.");
            try {
                if (FLAC__stream_encoder_set_channels(encoder, (uint)source.WaveFormat.Channels) == 0 ||
                    FLAC__stream_encoder_set_bits_per_sample(encoder, (uint)bits) == 0 ||
                    FLAC__stream_encoder_set_sample_rate(encoder, (uint)source.WaveFormat.SampleRate) == 0 ||
                    FLAC__stream_encoder_set_compression_level(encoder, 5) == 0 ||
                    FLAC__stream_encoder_init_stream(encoder, write, seek, tell, IntPtr.Zero, IntPtr.Zero) != 0)
                    throw new IOException("Could not initialize FLAC encoder.", failure);
                var samples = new float[8192 * source.WaveFormat.Channels];
                var pcm = new int[samples.Length];
                int count;
                while ((count = source.Read(samples, 0, samples.Length)) > 0) {
                    for (int i = 0; i < count; i++) {
                        if (!float.IsFinite(samples[i])) throw new InvalidDataException("Cannot encode non-finite audio.");
                        double value = Math.Clamp(samples[i], -1f, 1f);
                        // Preserve the previous 16-bit cache/export quantization; migrated PCM stays exact.
                        pcm[i] = bits == 16
                            ? preservePcm ? (int)Math.Clamp(Math.Round(value * 32768), -32768, 32767) : (short)((float)value * short.MaxValue)
                            : (int)Math.Clamp(Math.Round(value * 8388608), -8388608, 8388607);
                    }
                    if (FLAC__stream_encoder_process_interleaved(encoder, pcm, (uint)(count / source.WaveFormat.Channels)) == 0)
                        throw new IOException("FLAC encoding failed.", failure);
                }
                if (FLAC__stream_encoder_finish(encoder) == 0) throw new IOException("Could not finalize FLAC audio.", failure);
            } finally {
                FLAC__stream_encoder_delete(encoder);
                GC.KeepAlive(write); GC.KeepAlive(seek); GC.KeepAlive(tell);
            }
        }
    }
}
