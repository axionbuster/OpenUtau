using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.SignalChain {
    public sealed class ChordHelperPlaybackTone {
        public int GridTone { get; }
        public double Frequency { get; }

        public ChordHelperPlaybackTone(int gridTone, double frequency) {
            GridTone = gridTone;
            Frequency = frequency;
        }
    }

    public sealed class ChordHelperPlaybackEvent {
        public Guid Id { get; }
        public int StartSample { get; }
        public int EndSample { get; }
        public IReadOnlyList<ChordHelperPlaybackTone> Tones { get; }
        public float TrackScale { get; }
        public float PanLeft { get; }
        public float PanRight { get; }

        public ChordHelperPlaybackEvent(
            Guid id, int startSample, int endSample,
            IReadOnlyList<ChordHelperPlaybackTone> tones,
            float trackScale, float panLeft, float panRight) {
            Id = id;
            StartSample = startSample;
            EndSample = endSample;
            Tones = tones;
            TrackScale = trackScale;
            PanLeft = panLeft;
            PanRight = panRight;
        }
    }

    /// <summary>
    /// Immutable transport data captured away from the audio callback. Chord helpers
    /// remain absent from rendered singer audio and exported mixdowns.
    /// </summary>
    public sealed class ChordHelperPlaybackSnapshot {
        const int SampleRate = 44100;
        const int Channels = 2;
        public IReadOnlyList<ChordHelperPlaybackEvent> Events { get; }
        public int LastSample { get; }

        public ChordHelperPlaybackSnapshot(IEnumerable<ChordHelperPlaybackEvent> events) {
            Events = events.OrderBy(item => item.StartSample).ToArray();
            LastSample = Events.Count == 0 ? 0 : Events.Max(item => item.EndSample);
        }

        public static ChordHelperPlaybackSnapshot Create(UProject project, int trackNo = -1) {
            bool is31Edo = project.Is31Edo;
            int divisions = is31Edo ? 31 : 12;
            var events = new List<ChordHelperPlaybackEvent>();
            foreach (var part in project.parts.OfType<UVoicePart>()) {
                if (part.trackNo < 0 || part.trackNo >= project.tracks.Count ||
                    (trackNo >= 0 && part.trackNo != trackNo)) {
                    continue;
                }
                var track = project.tracks[part.trackNo];
                bool effectivelyMuted = track.Muted || track.Mute ||
                    (project.SoloTrackExist && !track.Solo);
                if (effectivelyMuted) {
                    continue;
                }
                float trackScale = PlaybackManager.DecibelToVolume(track.Volume);
                (float panLeft, float panRight) = MusicMath.PanToChannelVolumes((float)track.Pan);
                var occurrences = part.chordRegions.Count == 0
                    ? part.chordHelpers.Select(helper => new ChordOccurrence(
                        new UChordRegion { position = part.position, sourceDuration = int.MaxValue, duration = int.MaxValue },
                        helper, 0, part.position + helper.position, part.position + helper.End))
                    : part.chordRegions.SelectMany(region => ChordRegionExpander.Enumerate(
                        region, 0, region.End)).Select(item => item with {
                            StartTick = item.StartTick + part.position,
                            EndTick = item.EndTick + part.position,
                        });
                foreach (var occurrence in occurrences) {
                    var helper = occurrence.Helper;
                    if (helper.mute || helper.tones.Count == 0) {
                        continue;
                    }
                    int startTick = occurrence.StartTick;
                    int endTick = occurrence.EndTick;
                    int startSample = ToStereoSample(project.timeAxis.TickPosToMsPos(startTick));
                    int endSample = Math.Max(startSample + Channels,
                        ToStereoSample(project.timeAxis.TickPosToMsPos(endTick)));
                    int rootTone = helper.rootTone ?? (is31Edo ? 155 : 60) + Edo31.Mod(helper.root, divisions);
                    rootTone = rootTone - Edo31.Mod(rootTone, divisions) + Edo31.Mod(helper.root, divisions);
                    var toneClasses = helper.tones
                        .Select(tone => Edo31.Mod(tone.Offset(is31Edo), divisions))
                        .Distinct()
                        .ToArray();
                    int bassClass = helper.bass == null
                        ? 0
                        : Edo31.Mod(helper.bass.Offset(is31Edo), divisions);
                    int[] gridTones;
                    if (bassClass > 0 && rootTone + bassClass - divisions < 0) {
                        gridTones = toneClasses
                            .Select(toneClass => rootTone + toneClass + (toneClass < bassClass ? divisions : 0))
                            .OrderBy(tone => tone)
                            .ToArray();
                    } else {
                        gridTones = toneClasses
                            .Select(toneClass => rootTone + toneClass -
                                (bassClass > 0 && toneClass == bassClass ? divisions : 0))
                            .OrderBy(tone => tone)
                            .ToArray();
                    }
                    int maxGridTone = (is31Edo ? Edo31.MaxStep : 132) - 1;
                    while (gridTones[^1] > maxGridTone && gridTones[0] - divisions >= 0) {
                        for (int i = 0; i < gridTones.Length; i++) {
                            gridTones[i] -= divisions;
                        }
                    }
                    var tones = gridTones.Select(gridTone => new ChordHelperPlaybackTone(
                        gridTone,
                        project.ToneToFrequency(is31Edo ? gridTone * Edo31.StepTone : gridTone)))
                        .ToArray();
                    events.Add(new ChordHelperPlaybackEvent(
                        occurrence.PlaybackId, startSample, endSample, tones,
                        trackScale, panLeft, panRight));
                }
            }
            return new ChordHelperPlaybackSnapshot(events);
        }

        static int ToStereoSample(double milliseconds) =>
            Math.Max(0, (int)Math.Round(milliseconds * SampleRate / 1000.0) * Channels);
    }

    /// <summary>
    /// Live-only harmonic preview for chord helpers. Snapshot replacement is atomic;
    /// the callback only reads immutable scalar/event data.
    /// </summary>
    public sealed class ChordHelperPlaybackSource : ISignalSource {
        const int SampleRate = 44100;
        const int Channels = 2;
        const int AttackMs = 25;
        const int ResumeAttackMs = 5;
        const int ReleaseMs = 25;
        const float PreviewAmplitude = 0.16f;
        const float SharedHeadroom = 0.9f;
        const float LimiterThreshold = 0.45f;
        const float LimiterCeiling = 0.65f;
        const float CombinedCeiling = 0.98f;

        readonly struct VoiceKey : IEquatable<VoiceKey> {
            public readonly Guid EventId;
            public readonly int GridTone;
            public VoiceKey(Guid eventId, int gridTone) {
                EventId = eventId;
                GridTone = gridTone;
            }
            public bool Equals(VoiceKey other) => EventId == other.EventId && GridTone == other.GridTone;
            public override bool Equals(object? obj) => obj is VoiceKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(EventId, GridTone);
        }

        sealed class Voice {
            public readonly VoiceKey Key;
            public readonly HarmonicGenerator Generator;
            public readonly double Frequency;
            float currentLeft;
            float currentRight;
            float targetLeft;
            float targetRight;
            float[] scratch = Array.Empty<float>();

            public Voice(
                VoiceKey key, double frequency, int attackMs, int initialPosition,
                float targetLeft, float targetRight) {
                Key = key;
                Frequency = frequency;
                Generator = new HarmonicGenerator(
                    frequency, 1, attackMs, ReleaseMs, 0, initialPosition);
                currentLeft = this.targetLeft = targetLeft;
                currentRight = this.targetRight = targetRight;
            }

            public void SetTarget(float left, float right) {
                targetLeft = left;
                targetRight = right;
            }

            public void Read(float[] buffer, int offset, int count) {
                if (scratch.Length < count) {
                    scratch = new float[count];
                } else {
                    Array.Clear(scratch, 0, count);
                }
                Generator.Read(scratch, 0, count);
                int frames = count / Channels;
                for (int frame = 0; frame < frames; frame++) {
                    float amount = (frame + 1f) / frames;
                    float left = currentLeft + (targetLeft - currentLeft) * amount;
                    float right = currentRight + (targetRight - currentRight) * amount;
                    buffer[offset + frame * Channels] += scratch[frame * Channels] * left;
                    buffer[offset + frame * Channels + 1] += scratch[frame * Channels + 1] * right;
                }
                currentLeft = targetLeft;
                currentRight = targetRight;
            }
        }

        ChordHelperPlaybackSnapshot snapshot;
        readonly Dictionary<VoiceKey, Voice> active = new();
        readonly List<Voice> releasing = new();
        readonly List<VoiceKey> deactivateKeys = new();
        bool hasMixed;
        int stopRequested;
        float[] mixBuffer = Array.Empty<float>();

        public int ActiveVoiceCount => active.Count;
        public int ReleasingVoiceCount => releasing.Count;

        public ChordHelperPlaybackSource(ChordHelperPlaybackSnapshot snapshot) {
            this.snapshot = snapshot;
        }

        public void Publish(ChordHelperPlaybackSnapshot updated) => Volatile.Write(ref snapshot, updated);

        public void StopAll() {
            Volatile.Write(ref snapshot,
                new ChordHelperPlaybackSnapshot(Array.Empty<ChordHelperPlaybackEvent>()));
            Interlocked.Exchange(ref stopRequested, 1);
        }

        public bool IsReady(int position, int count) => true;

        public int Mix(int position, float[] buffer, int offset, int count) {
            if (Interlocked.Exchange(ref stopRequested, 0) != 0) {
                active.Clear();
                releasing.Clear();
            }
            var currentSnapshot = Volatile.Read(ref snapshot);
            bool producedAudio = active.Count > 0 || releasing.Count > 0;
            if (mixBuffer.Length < count) {
                mixBuffer = new float[count];
            } else {
                Array.Clear(mixBuffer, 0, count);
            }
            int cursor = position;
            int end = position + count;
            while (cursor < end) {
                Reconcile(currentSnapshot, cursor);
                producedAudio |= active.Count > 0 || releasing.Count > 0;
                int boundary = NextBoundary(currentSnapshot, cursor, end);
                int segmentCount = boundary - cursor;
                int segmentOffset = cursor - position;
                foreach (var voice in active.Values) {
                    voice.Read(mixBuffer, segmentOffset, segmentCount);
                }
                foreach (var voice in releasing) {
                    if (voice.Generator.isPlaying) {
                        voice.Read(mixBuffer, segmentOffset, segmentCount);
                    }
                }
                releasing.RemoveAll(voice => !voice.Generator.isPlaying);
                cursor = boundary;
            }
            for (int i = 0; i < count; i++) {
                buffer[offset + i] = AddWithHeadroom(buffer[offset + i], Limit(mixBuffer[i]));
            }
            hasMixed = true;
            bool hasFuture = false;
            for (int i = 0; i < currentSnapshot.Events.Count; i++) {
                if (currentSnapshot.Events[i].EndSample > end) {
                    hasFuture = true;
                    break;
                }
            }
            return hasFuture || producedAudio || active.Count > 0 || releasing.Count > 0 ? end : position;
        }

        void Reconcile(ChordHelperPlaybackSnapshot currentSnapshot, int position) {
            deactivateKeys.Clear();
            foreach (var key in active.Keys) {
                if (!HasTarget(currentSnapshot, position, key)) {
                    deactivateKeys.Add(key);
                }
            }
            foreach (var key in deactivateKeys) {
                var voice = active[key];
                voice.Generator.Stop();
                releasing.Add(voice);
                active.Remove(key);
            }
            foreach (var item in currentSnapshot.Events) {
                if (item.StartSample > position || position >= item.EndSample) {
                    continue;
                }
                foreach (var tone in item.Tones) {
                    var key = new VoiceKey(item.Id, tone.GridTone);
                    ReconcileVoice(key, item, tone, position);
                }
            }
        }

        void ReconcileVoice(
            VoiceKey key, ChordHelperPlaybackEvent item,
            ChordHelperPlaybackTone tone, int position) {
            int toneCount = item.Tones.Count;
            float toneAmplitude = PreviewAmplitude * MathF.Sqrt(2) * SharedHeadroom / MathF.Sqrt(toneCount);
            float left = toneAmplitude * item.TrackScale * item.PanLeft;
            float right = toneAmplitude * item.TrackScale * item.PanRight;
            if (active.TryGetValue(key, out var existing) &&
                Math.Abs(existing.Frequency - tone.Frequency) <=
                    Math.Max(1e-9, tone.Frequency * 1e-12)) {
                existing.SetTarget(left, right);
                return;
            }
            if (existing != null) {
                existing.Generator.Stop();
                releasing.Add(existing);
                active.Remove(key);
            }
            bool startsHere = item.StartSample == position;
            int attackMs = startsHere ? AttackMs : (hasMixed ? ResumeAttackMs : AttackMs);
            int initialPosition = !hasMixed && !startsHere
                ? Math.Max(0, (position - item.StartSample) / Channels)
                : 0;
            active[key] = new Voice(
                key, tone.Frequency, attackMs, initialPosition, left, right);
        }

        static bool HasTarget(
            ChordHelperPlaybackSnapshot snapshot, int position, VoiceKey key) {
            foreach (var item in snapshot.Events) {
                if (item.Id != key.EventId || item.StartSample > position || position >= item.EndSample) {
                    continue;
                }
                foreach (var tone in item.Tones) {
                    if (tone.GridTone == key.GridTone) {
                        return true;
                    }
                }
            }
            return false;
        }

        static int NextBoundary(ChordHelperPlaybackSnapshot snapshot, int position, int end) {
            int boundary = end;
            foreach (var item in snapshot.Events) {
                if (item.StartSample > position && item.StartSample < boundary) {
                    boundary = item.StartSample;
                }
                if (item.EndSample > position && item.EndSample < boundary) {
                    boundary = item.EndSample;
                }
            }
            // Every scheduled position is stereo-frame aligned.
            return Math.Max(position + Channels, boundary);
        }

        static float Limit(float sample) {
            float magnitude = MathF.Abs(sample);
            if (magnitude <= LimiterThreshold) {
                return sample;
            }
            float width = LimiterCeiling - LimiterThreshold;
            float limited = LimiterThreshold + width * MathF.Tanh((magnitude - LimiterThreshold) / width);
            return MathF.CopySign(limited, sample);
        }

        static float AddWithHeadroom(float existing, float helper) {
            float combined = existing + helper;
            if (MathF.Abs(combined) <= CombinedCeiling) {
                return combined;
            }
            // Never alter pre-existing rendered audio. Only trim the helper's
            // contribution when it would push an otherwise bounded mix over.
            if (MathF.Abs(existing) >= CombinedCeiling && MathF.Abs(combined) >= MathF.Abs(existing)) {
                return existing;
            }
            return MathF.CopySign(CombinedCeiling, combined);
        }
    }
}
