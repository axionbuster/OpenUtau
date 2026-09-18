using System;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.SignalChain {
    public enum SlotState {
        /// <summary>No PCM yet. In hold mode, playback waits for this slot.</summary>
        Pending = 0,
        /// <summary>PCM present.</summary>
        Ready = 1,
        /// <summary>Render failed or was cancelled. Never blocks playback; contributes silence.</summary>
        Failed = 2,
    }

    /// <summary>
    /// One immutable PCM placement (a phrase or a wave part) within a track.
    ///
    /// Offsets are in source-sample units; <c>copies = 2 / channels</c> places
    /// a mono source into the interleaved stereo transport domain by duplicating
    /// each sample into both channels inside the mix loop.
    ///
    /// A slot never mutates after construction: each plan version is a fresh array
    /// of slot objects referencing the same <see cref="Frozen{T}"/> buffers.
    /// </summary>
    public sealed class SampleSlot {
        private const int ChannelsInTransport = 2;
        public readonly int Offset;          // source-sample units
        public readonly int EstimatedLength; // source-sample units (layout estimate, not data length)
        public readonly int Channels;        // 1 (mono, duplicated to stereo) or 2
        public readonly double EndMs;        // offsetMs + estimatedLengthMs (window filtering only)

        /// <summary>null unless <see cref="State"/> is Ready.</summary>
        public readonly Frozen<float> Data;
        public readonly SlotState State;
        // Session-local click suppression. These positions are in the interleaved
        // stereo transport domain and never alter the frozen/durable PCM.
        public readonly int PublicationFadeStart;
        public readonly int PublicationFadeLength;

        public SampleSlot(double offsetMs, double estimatedLengthMs, int channels)
            : this(offsetMs, estimatedLengthMs, 0, channels, null, SlotState.Pending) { }

        public SampleSlot(
                double offsetMs, double estimatedLengthMs, int channels,
                Frozen<float> data, SlotState state)
            : this(offsetMs, estimatedLengthMs, 0, channels, data, state) { }

        public SampleSlot(
                double offsetMs, double estimatedLengthMs, double skipOverMs, int channels,
                Frozen<float> data, SlotState state,
                int publicationFadeStart = -1, int publicationFadeLength = 0) {
            Offset = (int)((offsetMs - skipOverMs) * 44100 / 1000) * channels;
            EstimatedLength = (int)(estimatedLengthMs * 44100 / 1000) * channels;
            Channels = channels;
            EndMs = offsetMs + estimatedLengthMs;
            Data = data;
            State = state;
            PublicationFadeStart = publicationFadeStart;
            PublicationFadeLength = publicationFadeLength;
        }

        private SampleSlot(
                int offset, int estimatedLength, int channels,
                Frozen<float> data, SlotState state,
                double endMs, int publicationFadeStart, int publicationFadeLength) {
            Offset = offset;
            EstimatedLength = estimatedLength;
            Channels = channels;
            EndMs = endMs;
            Data = data;
            State = state;
            PublicationFadeStart = publicationFadeStart;
            PublicationFadeLength = publicationFadeLength;
        }

        public bool HasSamples => Data != null;

        internal SampleSlot WithPublicationFade(int transportPosition, int fadeFrames) {
            int slotStart = Offset * (ChannelsInTransport / Channels);
            // Begin at the next transport frame that can be heard. Aligning the
            // stereo pair gives both channels exactly the same envelope.
            int start = Math.Max(slotStart, transportPosition);
            start -= start % ChannelsInTransport;
            return new SampleSlot(Offset, EstimatedLength, Channels, Data, State, EndMs,
                start, fadeFrames * ChannelsInTransport);
        }

        internal SlotState ReadinessAt(int position, int count, out bool overlaps) {
            int copies = ChannelsInTransport / Channels;
            overlaps = position + count > Offset * copies
                && Offset * copies + EstimatedLength * copies > position;
            return State;
        }

        // Readiness: outside the slot's window the transport does not depend on the
        // slot at all. Inside the window, only Pending blocks — Failed must never
        // hold playback forever; it contributes silence.
        public bool IsReady(int position, int count) {
            int copies = 2 / Channels;
            return position + count <= Offset * copies
                || Offset * copies + EstimatedLength * copies <= position
                || State != SlotState.Pending;
        }

        // Mix over the frozen buffer. A null-data slot (Pending/Failed) reads as
        // silence through its estimated end in every mode — hold mode never mixes
        // a Pending slot inside its window (the IsReady gate runs first), and
        // passthrough (loop) mode needs the silence so a pending phrase does not
        // truncate the stream. Past the estimated end it must report position so
        // MasterExhausted can fire.
        public int Mix(int position, float[] buffer, int index, int count) {
            int copies = 2 / Channels;
            if (Data == null) {
                if (position + count <= Offset * copies) {
                    return position + count;
                }
                int estimatedEnd = Offset * copies + EstimatedLength * copies;
                if (estimatedEnd <= position) {
                    return position;
                }
                return Math.Min(position + count, estimatedEnd);
            }
            int start = Math.Max(position, Offset * copies);
            int end = Math.Min(position + count, Offset * copies + Data.Length * copies);
            for (int i = start; i < end; ++i) {
                float gain = 1;
                if (PublicationFadeLength > 0
                        && i >= PublicationFadeStart
                        && i < PublicationFadeStart + PublicationFadeLength) {
                    int elapsedFrames = (i - PublicationFadeStart) / ChannelsInTransport;
                    int fadeFrames = PublicationFadeLength / ChannelsInTransport;
                    gain = Math.Clamp(elapsedFrames / (float)Math.Max(1, fadeFrames - 1), 0, 1);
                }
                buffer[index + i - position] += Data.Buffer[i / copies - Offset] * gain;
            }
            return end;
        }
    }

    /// <summary>
    /// <see cref="ISignalSource"/> over a set of <see cref="SampleSlot"/>s.
    ///
    /// The slot array is swapped atomically by the <see cref="MixPlanner"/> (a volatile
    /// reference); the callback captures the reference once per call, so it never
    /// observes a torn set. <c>Mix</c>/<c>IsReady</c> are the same Max / all-composition
    /// <see cref="WaveMix"/> does over its sources.
    /// </summary>
    public class SlotMixSource : ISignalSource {
        private const int PublicationFadeFrames = 44100 * 20 / 1000;
        private volatile SampleSlot[] slots = Array.Empty<SampleSlot>();
        private volatile int lastMixPosition = -1;

        /// <summary>Current slots; exposed for UI readers (waveform canvas, DAW).</summary>
        public SampleSlot[] CurrentSlots => slots;

        public void SetSlots(SampleSlot[] newSlots) {
            var previous = slots;
            int publicationPosition = lastMixPosition;
            if (publicationPosition >= 0) {
                int count = Math.Min(previous.Length, newSlots.Length);
                for (int i = 0; i < count; ++i) {
                    bool samePlacement = previous[i].Offset == newSlots[i].Offset
                        && previous[i].EstimatedLength == newSlots[i].EstimatedLength
                        && previous[i].Channels == newSlots[i].Channels;
                    if (samePlacement
                            && previous[i].State == SlotState.Ready
                            && newSlots[i].State == SlotState.Ready
                            && ReferenceEquals(previous[i].Data, newSlots[i].Data)) {
                        // Preserve an in-flight publication envelope through an
                        // unrelated planner rebuild without restarting it.
                        newSlots[i] = previous[i];
                    } else if (previous[i].State == SlotState.Pending
                            && newSlots[i].State == SlotState.Ready
                            && samePlacement
                            && publicationPosition >= newSlots[i].Offset * (2 / newSlots[i].Channels)
                            && publicationPosition < (newSlots[i].Offset + newSlots[i].EstimatedLength) * (2 / newSlots[i].Channels)) {
                        newSlots[i] = newSlots[i].WithPublicationFade(
                            publicationPosition, PublicationFadeFrames);
                    }
                }
            }
            slots = newSlots;
        }

        public bool IsReady(int position, int count) {
            var s = slots;
            for (int i = 0; i < s.Length; ++i) {
                if (!s[i].IsReady(position, count)) {
                    return false;
                }
            }
            return true;
        }

        public int Mix(int position, float[] buffer, int index, int count) {
            var s = slots;
            if (s.Length == 0) {
                return 0;
            }
            int max = s[0].Mix(position, buffer, index, count);
            for (int i = 1; i < s.Length; ++i) {
                int p = s[i].Mix(position, buffer, index, count);
                if (p > max) {
                    max = p;
                }
            }
            lastMixPosition = max;
            return max;
        }
    }
}
