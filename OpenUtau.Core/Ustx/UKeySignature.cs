using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Util;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    // Absolute project ticks. Key is a pitch class in 12-TET, a fifths index in 31-TET.
    public class UKeySignature {
        public int position;
        public int key;
        public bool major = true;
        public bool minor = true;
        [YamlIgnore] public string ModeName => major ? (minor ? "Major + Minor" : "Major")
            : minor ? "Minor" : "Chromatic";
        public UKeySignature Clone() => (UKeySignature)MemberwiseClone();
        public string Label(bool is31) => (is31 ? Edo31.FifthName(key)
            : MusicMath.KeysInOctave[key].Item1) + " " + ModeName;
        public bool Contains(int step, bool is31) {
            if (!major && !minor) return true;
            if (is31) return Edo31.IsDegreeInSelectedScales(Edo31.ScaleColorIndex(step, key), major, minor);
            int degree = Edo31.Mod(step - key, 12);
            return (major && degree is 0 or 2 or 4 or 5 or 7 or 9 or 11)
                || (minor && degree is 0 or 2 or 3 or 5 or 7 or 8 or 10);
        }
        public static void Validate(IReadOnlyList<UKeySignature> changes, bool is31) {
            if (changes == null || changes.Count == 0 || changes[0] == null || changes[0].position != 0)
                throw new FileFormatException("Key signatures must begin at project tick zero.");
            int previous = -1;
            foreach (var change in changes) {
                if (change == null || change.position <= previous || change.key < (is31 ? -15 : 0)
                        || change.key > (is31 ? 15 : 11))
                    throw new FileFormatException("Invalid or unordered key signature.");
                previous = change.position;
            }
        }
    }
}
