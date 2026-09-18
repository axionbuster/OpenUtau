using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class ToneGeneratorTest {
        private const int SampleRate = 44100;
        private static readonly double[] HarmonicAmplitudes = { 1, 0.5, 0.25, 0.125, 0.0625 };
        private const double Normalization = 1 + 0.5 + 0.25 + 0.125 + 0.0625;

        public static IEnumerable<object[]> EditorPitches() {
            for (int tone = 0; tone < 132; tone++) {
                yield return new object[] { false, tone };
            }
            for (int step = 0; step < Edo31.MaxStep; step++) {
                yield return new object[] { true, step };
            }
        }

        [Theory]
        [MemberData(nameof(EditorPitches))]
        public void EveryEditorPitchProducesTheExpectedSamples(bool edo31, int gridTone) {
            double expectedTone = edo31 ? gridTone * 12.0 / 31 : gridTone;
            double expectedFrequency = 440.0 * Math.Pow(2, (expectedTone - 69) / 12.0);
            var note = UNote.Create();
            note.tone = edo31 ? (int)Math.Round(expectedTone) : gridTone;
            note.tone31 = edo31 ? gridTone : null;
            Assert.Equal(expectedTone, note.PreciseAdjustedTone, 12);
            double actualFrequency = MusicMath.ToneToFreq(note.PreciseAdjustedTone);
            Assert.InRange(Math.Abs(actualFrequency - expectedFrequency), 0, expectedFrequency * 1e-12);

            var generator = new HarmonicGenerator(expectedFrequency, 1, 1, 25);
            var buffer = new float[2 * 64];
            generator.Read(new float[2 * 45], 0, 2 * 45); // Finish the 44.1-sample attack.
            generator.Read(buffer, 0, buffer.Length);

            for (int frame = 0; frame < buffer.Length / 2; frame++) {
                int position = frame + 45;
                double phase = position * 2 * Math.PI * expectedFrequency / SampleRate;
                double expected = 0;
                for (int i = 0; i < HarmonicAmplitudes.Length; i++) {
                    int harmonic = i + 1;
                    if (expectedFrequency * harmonic >= SampleRate / 2.0) {
                        break;
                    }
                    expected += HarmonicAmplitudes[i] * Math.Sin(phase * harmonic);
                }
                expected /= Normalization;
                Assert.InRange(Math.Abs(buffer[frame * 2] - expected), 0, 1e-6);
                Assert.Equal(buffer[frame * 2], buffer[frame * 2 + 1]);
            }
        }

        [Fact]
        public void NotePreviewContainsFundamentalAndHarmonicsAtCorrectFrequencies() {
            const double fundamental = 440;
            var generator = new HarmonicGenerator(fundamental, 1);
            generator.Read(new float[2 * 1103], 0, 2 * 1103); // Finish the 25 ms attack.
            var buffer = new float[2 * SampleRate];
            generator.Read(buffer, 0, buffer.Length);

            double fundamentalAmplitude = MeasureAmplitude(buffer, fundamental);
            Assert.InRange(fundamentalAmplitude, 1.0 / Normalization - 1e-4, 1.0 / Normalization + 1e-4);
            for (int harmonic = 2; harmonic <= HarmonicAmplitudes.Length; harmonic++) {
                double actual = MeasureAmplitude(buffer, fundamental * harmonic);
                double expected = HarmonicAmplitudes[harmonic - 1] / Normalization;
                Assert.InRange(actual, expected - 1e-4, expected + 1e-4);
            }
            Assert.InRange(MeasureAmplitude(buffer, fundamental * 1.5), 0, 1e-4);
        }

        [Fact]
        public void ReferencePitchesMatchBothEditorTunings() {
            Assert.Equal(440, MusicMath.ToneToFreq(69), 12);
            Assert.Equal(261.6255653005986, MusicMath.ToneToFreq(155 * Edo31.StepTone), 10);
            Assert.Equal(437.5473070250114, MusicMath.ToneToFreq(178 * Edo31.StepTone), 10);
            Assert.Equal(447.4408797803159, MusicMath.ToneToFreq(179 * Edo31.StepTone), 10);
        }

        private static double MeasureAmplitude(float[] stereoBuffer, double frequency) {
            double sin = 0;
            double cos = 0;
            int frames = stereoBuffer.Length / 2;
            for (int frame = 0; frame < frames; frame++) {
                double phase = 2 * Math.PI * frequency * frame / SampleRate;
                double sample = stereoBuffer[frame * 2];
                sin += sample * Math.Sin(phase);
                cos += sample * Math.Cos(phase);
            }
            return 2 * Math.Sqrt(sin * sin + cos * cos) / frames;
        }
    }
}
