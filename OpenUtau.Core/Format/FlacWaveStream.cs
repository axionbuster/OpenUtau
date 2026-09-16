using System;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace OpenUtau.Core.Format {
    /// <summary>Streaming libFLAC decoder, including final partial frames and sample-accurate seeking.</summary>
    internal sealed class FlacWaveStream : WaveStream {
        const string Lib = "FLAC";
        readonly FileStream input;
        readonly IntPtr decoder;
        readonly ReadCallback read;
        readonly SeekCallback seek;
        readonly TellCallback tell;
        readonly LengthCallback length;
        readonly EofCallback eof;
        readonly WriteCallback write;
        readonly ErrorCallback error;
        readonly MetadataCallback metadata;
        byte[] pending = Array.Empty<byte>();
        int pendingOffset;
        long position;
        Exception? failure;
        bool disposed;
        public override WaveFormat WaveFormat { get; }
        public override long Length { get; }
        public override long Position {
            get => position;
            set {
                if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value));
                pending = Array.Empty<byte>(); pendingOffset = 0;
                if (value < Length && FLAC__stream_decoder_seek_absolute(decoder, (ulong)(value / WaveFormat.BlockAlign)) == 0)
                    throw new IOException("Could not seek in FLAC audio.", failure);
                position = value - value % WaveFormat.BlockAlign;
            }
        }
        public FlacWaveStream(string path) {
            AudioExport.EnsureNativeLoaded();
            input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            decoder = FLAC__stream_decoder_new();
            read = (IntPtr _, IntPtr buffer, ref UIntPtr bytes, IntPtr _) => {
                try {
                    int count;
                    unsafe { count = input.Read(new Span<byte>(buffer.ToPointer(), checked((int)bytes.ToUInt64()))); }
                    bytes = (UIntPtr)count; return count == 0 ? 1 : 0;
                } catch (Exception e) { failure = e; bytes = UIntPtr.Zero; return 2; }
            };
            seek = (_, offset, _) => { try { input.Position = checked((long)offset); return 0; } catch (Exception e) { failure = e; return 1; } };
            tell = (IntPtr _, out ulong offset, IntPtr _) => { offset = (ulong)input.Position; return 0; };
            length = (IntPtr _, out ulong size, IntPtr _) => { size = (ulong)input.Length; return 0; };
            eof = (_, _) => input.Position >= input.Length ? 1 : 0;
            write = (_, frame, buffers, _) => {
                try {
                    int frames = Marshal.ReadInt32(frame); // FLAC__FrameHeader begins with blocksize.
                    int channels = (int)FLAC__stream_decoder_get_channels(decoder);
                    int bits = (int)FLAC__stream_decoder_get_bits_per_sample(decoder);
                    var samples = new float[checked(frames * channels)];
                    double scale = Math.Pow(2, bits - 1);
                    unsafe {
                        for (int c = 0; c < channels; c++) {
                            int* channel = (int*)Marshal.ReadIntPtr(buffers, c * IntPtr.Size);
                            for (int f = 0; f < frames; f++) samples[f * channels + c] = (float)(channel[f] / scale);
                        }
                    }
                    pending = new byte[samples.Length * sizeof(float)];
                    Buffer.BlockCopy(samples, 0, pending, 0, pending.Length); pendingOffset = 0;
                    return 0;
                } catch (Exception e) { failure = e; return 1; }
            };
            error = (_, status, _) => failure = new InvalidDataException($"Invalid FLAC frame ({status}).");
            int sampleRate = 0, channelCount = 0;
            ulong totalSamples = 0;
            metadata = (_, block, _) => {
                if (Marshal.ReadInt32(block) == 0) { // STREAMINFO; union aligned at byte 16.
                    sampleRate = Marshal.ReadInt32(block, 32);
                    channelCount = Marshal.ReadInt32(block, 36);
                    totalSamples = (ulong)Marshal.ReadInt64(block, 48);
                }
            };
            try {
                if (decoder == IntPtr.Zero || FLAC__stream_decoder_init_stream(decoder, read, seek, tell, length, eof, write, metadata, error, IntPtr.Zero) != 0 ||
                    FLAC__stream_decoder_process_until_end_of_metadata(decoder) == 0)
                    throw new InvalidDataException("Could not open FLAC audio.", failure);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
                Length = checked((long)totalSamples * WaveFormat.BlockAlign);
            } catch { Dispose(); throw; }
        }
        public override int Read(byte[] buffer, int offset, int count) {
            int total = 0;
            count = (int)Math.Min(count, Length - position);
            while (total < count) {
                if (pendingOffset == pending.Length) {
                    pending = Array.Empty<byte>(); pendingOffset = 0;
                    if (FLAC__stream_decoder_process_single(decoder) == 0 || failure != null)
                        throw new InvalidDataException("Could not decode FLAC audio.", failure);
                    if (pending.Length == 0 && FLAC__stream_decoder_get_state(decoder) == 4)
                        throw new EndOfStreamException("FLAC audio ended before its declared sample count.");
                    continue;
                }
                int n = Math.Min(count - total, pending.Length - pendingOffset);
                Buffer.BlockCopy(pending, pendingOffset, buffer, offset + total, n);
                pendingOffset += n; total += n; position += n;
            }
            return total;
        }
        protected override void Dispose(bool disposing) {
            if (!disposed) {
                disposed = true;
                if (decoder != IntPtr.Zero) FLAC__stream_decoder_delete(decoder);
                input.Dispose();
            }
            base.Dispose(disposing);
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReadCallback(IntPtr d, IntPtr b, ref UIntPtr n, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SeekCallback(IntPtr d, ulong n, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TellCallback(IntPtr d, out ulong n, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int LengthCallback(IntPtr d, out ulong n, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EofCallback(IntPtr d, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int WriteCallback(IntPtr d, IntPtr f, IntPtr b, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void MetadataCallback(IntPtr d, IntPtr m, IntPtr c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ErrorCallback(IntPtr d, int e, IntPtr c);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FLAC__stream_decoder_new();
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern void FLAC__stream_decoder_delete(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern int FLAC__stream_decoder_init_stream(IntPtr d, ReadCallback r, SeekCallback s, TellCallback t, LengthCallback l, EofCallback e, WriteCallback w, MetadataCallback m, ErrorCallback error, IntPtr c);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern int FLAC__stream_decoder_process_until_end_of_metadata(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern int FLAC__stream_decoder_process_single(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern int FLAC__stream_decoder_seek_absolute(IntPtr d, ulong sample);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern int FLAC__stream_decoder_get_state(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern uint FLAC__stream_decoder_get_channels(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern uint FLAC__stream_decoder_get_bits_per_sample(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern uint FLAC__stream_decoder_get_sample_rate(IntPtr d);
        [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] static extern ulong FLAC__stream_decoder_get_total_samples(IntPtr d);
    }
}
