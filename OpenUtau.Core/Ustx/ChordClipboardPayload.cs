using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Ustx {
    public sealed class ChordClipboardPayload {
        public List<UChordHelper> Helpers { get; }
        public bool Is31Edo { get; }
        public Edo31PitchReference? PitchReference31 { get; }
        public int AnchorTick { get; }

        public ChordClipboardPayload(IEnumerable<UChordHelper> helpers, bool is31Edo,
                Edo31PitchReference? pitchReference31) {
            Helpers = helpers.Select(helper => helper.Clone()).ToList();
            Is31Edo = is31Edo;
            PitchReference31 = pitchReference31?.ValidatedCopy();
            AnchorTick = Helpers.Count == 0 ? 0 : Helpers.Min(helper => helper.position);
        }

        public List<UChordHelper>? CloneForPaste(UProject project, int targetTick) {
            if (Helpers.Count == 0 || Is31Edo != project.Is31Edo ||
                (project.Is31Edo && (PitchReference31 == null ||
                 !PitchReference31.HasSameTuning(project.PitchReference31))) ||
                Helpers.Any(helper => helper.duration <= 0 || helper.position < 0 ||
                    helper.tones == null || helper.tones.Any(tone => tone == null || tone.degree <= 0))) {
                return null;
            }
            int delta = targetTick - AnchorTick;
            var result = Helpers.Select(helper => helper.Clone()).ToList();
            if (result.Any(helper => helper.position + delta < 0)) {
                return null;
            }
            foreach (var helper in result) {
                helper.position += delta;
                helper.Normalize(project.Is31Edo);
            }
            return result;
        }
    }
}
