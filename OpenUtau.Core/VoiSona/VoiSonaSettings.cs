using System;
using System.IO;

namespace OpenUtau.Core.VoiSona {
    // Track-level native controls. Defaults preserve the original adapter's exact pitch.
    public sealed class VoiSonaSettings {
        public bool NativePitch { get; set; }
        public double PitchAccuracy { get; set; } = 1;
        public double VibratoAmplitude { get; set; }
        public double VibratoFrequency { get; set; } = 1;
        public double Age { get; set; }
        public double Huskiness { get; set; }

        public VoiSonaSettings Clone() => (VoiSonaSettings)MemberwiseClone();
        public VoiSonaSettings ValidatedCopy() {
            var copy = Clone();
            copy.PitchAccuracy = Bound(PitchAccuracy, -1, 1);
            copy.VibratoAmplitude = Bound(VibratoAmplitude, 0, 10);
            copy.VibratoFrequency = Bound(VibratoFrequency, 0, 2);
            copy.Age = Bound(Age, -1, 1);
            copy.Huskiness = Bound(Huskiness, -10, 10);
            return copy;
        }
        static double Bound(double value, double min, double max) => double.IsFinite(value)
            ? Math.Clamp(value, min, max) : throw new ArgumentException("VoiSona parameters must be finite.");
        internal void WriteHash(BinaryWriter writer) {
            writer.Write(NativePitch); writer.Write(PitchAccuracy);
            writer.Write(VibratoAmplitude); writer.Write(VibratoFrequency);
            writer.Write(Age); writer.Write(Huskiness);
        }
    }
}
