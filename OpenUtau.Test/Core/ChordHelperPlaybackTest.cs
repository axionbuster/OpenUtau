using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using OpenUtau.Core.Format;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class ChordHelperPlaybackTest {
        const int SampleRate = 44100;

        static UProject Project(bool is31Edo, UChordHelper helper) {
            var project = Format.Ustx.Create();
            project.Is31Edo = is31Edo;
            project.tracks.Clear();
            project.tracks.Add(new UTrack("Chord") { TrackNo = 0 });
            project.parts.Clear();
            project.parts.Add(new UVoicePart {
                trackNo = 0,
                position = 0,
                duration = 1920,
                chordHelpers = new List<UChordHelper> { helper },
            });
            project.AfterLoad();
            return project;
        }

        [Theory]
        [InlineData(false, 60, 52, 60, 67)]
        [InlineData(true, 155, 134, 155, 173)]
        public void SnapshotUsesStoredRegisterExactTuningAndInversion(
            bool is31Edo, int rootTone, params int[] expectedTones) {
            var helper = new UChordHelper {
                duration = 960,
                root = 0,
                rootTone = rootTone,
                tones = ChordHelperTheory.CreatePreset("Major"),
                bass = new UChordInterval(3),
            };
            var project = Project(is31Edo, helper);
            var item = Assert.Single(ChordHelperPlaybackSnapshot.Create(project).Events);
            Assert.Equal(expectedTones, item.Tones.Select(tone => tone.GridTone));
            Assert.All(item.Tones, tone => Assert.Equal(
                project.ToneToFrequency(is31Edo ? tone.GridTone * Edo31.StepTone : tone.GridTone),
                tone.Frequency, 10));
        }

        [Fact]
        public void SnapshotRespectsHelperTrackMuteSoloAndTempo() {
            var helper = new UChordHelper {
                position = 480,
                duration = 480,
                rootTone = 60,
                tones = ChordHelperTheory.CreatePreset("Major"),
            };
            var project = Project(false, helper);
            project.tracks[0].Volume = -6;
            project.tracks[0].Pan = -100;
            var item = Assert.Single(ChordHelperPlaybackSnapshot.Create(project).Events);
            Assert.Equal(SampleRate / 2, item.StartSample / 2);
            Assert.Equal(SampleRate, item.EndSample / 2);
            Assert.Equal(PlaybackManager.DecibelToVolume(-6), item.TrackScale);
            Assert.Equal(1, item.PanLeft, 6);
            Assert.Equal(0, item.PanRight, 6);

            helper.mute = true;
            Assert.Empty(ChordHelperPlaybackSnapshot.Create(project).Events);
            helper.mute = false;
            project.tracks[0].Mute = true;
            Assert.Empty(ChordHelperPlaybackSnapshot.Create(project).Events);
            project.tracks[0].Mute = false;
            project.tracks[0].Muted = true;
            Assert.Empty(ChordHelperPlaybackSnapshot.Create(project).Events);
            project.tracks[0].Muted = false;
            project.tracks.Add(new UTrack("Solo") { TrackNo = 1, Solo = true });
            Assert.Empty(ChordHelperPlaybackSnapshot.Create(project).Events);
        }

        [Theory]
        [InlineData(false, 0, 4, 7, 12)]
        [InlineData(true, 0, 10, 18, 31)]
        public void LowInversionStaysInsideTheEditorRange(
            bool is31Edo, int rootTone, params int[] expectedTones) {
            var project = Project(is31Edo, new UChordHelper {
                duration = 480,
                root = 0,
                rootTone = rootTone,
                tones = ChordHelperTheory.CreatePreset("Major"),
                bass = new UChordInterval(3),
            });
            var item = Assert.Single(ChordHelperPlaybackSnapshot.Create(project).Events);
            Assert.Equal(expectedTones, item.Tones.Select(tone => tone.GridTone));
        }

        [Fact]
        public void AtomicMuteReleasesAndUnmuteInsideDurationResumes() {
            var snapshot = ManualSnapshot(new[] { 261.625565, 329.627557, 391.995436 });
            var source = new ChordHelperPlaybackSource(snapshot);
            int position = 0;
            var first = new float[2048];
            position = source.Mix(position, first, 0, first.Length);
            Assert.Equal(3, source.ActiveVoiceCount);

            source.Publish(new ChordHelperPlaybackSnapshot(Array.Empty<ChordHelperPlaybackEvent>()));
            var release = new float[4096];
            int releaseStart = position;
            position = source.Mix(position, release, 0, release.Length);
            Assert.Equal(releaseStart + release.Length, position);
            Assert.Equal(0, source.ActiveVoiceCount);
            Assert.Contains(release, sample => Math.Abs(sample) > 1e-6);
            var finishRelease = new float[4096];
            int exhausted = source.Mix(position, finishRelease, 0, finishRelease.Length);
            Assert.Equal(position, exhausted);
            Assert.Equal(0, source.ReleasingVoiceCount);

            source.Publish(snapshot);
            var resumed = new float[2048];
            source.Mix(position, resumed, 0, resumed.Length);
            Assert.Equal(3, source.ActiveVoiceCount);
            Assert.InRange(Math.Abs(resumed[0]), 0, 1e-7);
            Assert.Contains(resumed.Skip(64), sample => Math.Abs(sample) > 1e-6);

            source.StopAll();
            var stopped = new float[512];
            source.Mix(position + resumed.Length, stopped, 0, stopped.Length);
            Assert.Equal(0, source.ActiveVoiceCount);
            Assert.Equal(0, source.ReleasingVoiceCount);
            Assert.All(stopped, sample => Assert.Equal(0, sample));
        }

        [Fact]
        public void SnapshotFrequencyChangeRestartsTheExactVoice() {
            var id = Guid.NewGuid();
            ChordHelperPlaybackSnapshot At(double frequency) => new(new[] {
                new ChordHelperPlaybackEvent(id, 0, SampleRate * 8,
                    new[] { new ChordHelperPlaybackTone(155, frequency) },
                    1, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)),
            });
            var source = new ChordHelperPlaybackSource(At(440));
            var first = new float[2048];
            int position = source.Mix(0, first, 0, first.Length);
            source.Publish(At(442));
            var retuned = new float[2048];
            source.Mix(position, retuned, 0, retuned.Length);
            Assert.Equal(1, source.ActiveVoiceCount);
            Assert.Equal(1, source.ReleasingVoiceCount);
        }

        [Fact]
        public void SmartGainKeepsRepresentativeChordsComparableAndBoundsOverlaps() {
            var cases = new[] {
                new[] { 130.812783 },
                new[] { 261.625565 },
                new[] { 261.625565, 329.627557, 391.995436 },
                new[] { 261.625565, 329.627557, 391.995436, 466.163762 },
                new[] { 523.251131, 783.990872 },
            };
            var rendered = cases.Select(RenderSustained).ToArray();
            var levels = rendered.Select(result => result.Rms).ToArray();
            Assert.True(levels.Max() / levels.Min() < 1.45,
                $"Chord RMS range was {levels.Min():F6}..{levels.Max():F6}");

            string? output = Environment.GetEnvironmentVariable("OPENUTAU_AUDIO_QA_OUTPUT");
            if (!string.IsNullOrEmpty(output)) {
                Directory.CreateDirectory(output);
                var silence = new float[SampleRate / 3 * 2];
                var joined = rendered.SelectMany(result => result.Samples.Concat(silence)).ToArray();
                using (var writer = new WaveFileWriter(
                    Path.Combine(output, "chord-helper-level-comparison.wav"),
                    WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2))) {
                    writer.WriteSamples(joined, 0, joined.Length);
                }
                File.WriteAllLines(Path.Combine(output, "chord-helper-level-measurements.txt"),
                    cases.Select((frequencies, index) =>
                        $"tones={frequencies.Length} rms={rendered[index].Rms:F6} peak={rendered[index].Peak:F6}"));
            }

            var events = Enumerable.Range(0, 8)
                .Select(_ => ManualEvent(new[] { 261.625565, 329.627557, 391.995436 }))
                .ToArray();
            var source = new ChordHelperPlaybackSource(new ChordHelperPlaybackSnapshot(events));
            var warmup = new float[SampleRate / 5 * 2];
            int position = source.Mix(0, warmup, 0, warmup.Length);
            var mixed = Enumerable.Repeat(0.8f, SampleRate / 10 * 2).ToArray();
            source.Mix(position, mixed, 0, mixed.Length);
            Assert.All(mixed, sample => Assert.InRange(sample, -0.98f, 0.98f));
        }

        static (float[] Samples, double Rms, double Peak) RenderSustained(double[] frequencies) {
            var source = new ChordHelperPlaybackSource(ManualSnapshot(frequencies));
            var warmup = new float[SampleRate / 5 * 2];
            int position = source.Mix(0, warmup, 0, warmup.Length);
            var measured = new float[SampleRate * 2];
            source.Mix(position, measured, 0, measured.Length);
            double rms = Math.Sqrt(Enumerable.Range(0, measured.Length / 2)
                .Select(frame => (double)measured[frame * 2] * measured[frame * 2])
                .Average());
            return (measured, rms, measured.Max(sample => Math.Abs((double)sample)));
        }

        static ChordHelperPlaybackSnapshot ManualSnapshot(double[] frequencies) =>
            new ChordHelperPlaybackSnapshot(new[] { ManualEvent(frequencies) });

        static ChordHelperPlaybackEvent ManualEvent(double[] frequencies) =>
            new ChordHelperPlaybackEvent(
                Guid.NewGuid(), 0, SampleRate * 8,
                frequencies.Select((frequency, index) =>
                    new ChordHelperPlaybackTone(60 + index, frequency)).ToArray(),
                1, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f));
    }
}
