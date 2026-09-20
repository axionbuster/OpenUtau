using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Ustx {
    public sealed class ChordClipboardPayload {
        public List<UChordHelper> Helpers { get; }
        public List<UChordRegion> Regions { get; }
        public bool Is31Edo { get; }
        public Edo31PitchReference? PitchReference31 { get; }
        public int AnchorTick { get; }

        public ChordClipboardPayload(IEnumerable<UChordHelper> helpers, bool is31Edo,
                Edo31PitchReference? pitchReference31) {
            Helpers = helpers.Select(helper => helper.Clone()).ToList();
            Regions = new List<UChordRegion>();
            Is31Edo = is31Edo;
            PitchReference31 = pitchReference31?.ValidatedCopy();
            AnchorTick = Helpers.Count == 0 ? 0 : Helpers.Min(helper => helper.position);
        }

        public ChordClipboardPayload(IEnumerable<UChordRegion> regions, bool is31Edo,
                Edo31PitchReference? pitchReference31) {
            Regions = regions.Select(region => region.Clone()).ToList();
            Helpers = new List<UChordHelper>();
            Is31Edo = is31Edo;
            PitchReference31 = pitchReference31?.ValidatedCopy();
            AnchorTick = Regions.Count == 0 ? 0 : Regions.Min(region => region.position);
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

        public List<UChordRegion>? CloneRegionsForPaste(UProject project, int targetTick) {
            if (Regions.Count == 0 || Is31Edo != project.Is31Edo ||
                (project.Is31Edo && (PitchReference31 == null ||
                 !PitchReference31.HasSameTuning(project.PitchReference31))) ||
                Regions.Any(region => region.position < 0 || region.sourceDuration <= 0 || region.duration <= 0 ||
                    (long)region.position + region.duration > int.MaxValue || region.chordHelpers == null ||
                    region.chordHelpers.Any(helper => helper.duration <= 0 || helper.position < 0 ||
                        helper.tones == null || helper.tones.Any(tone => tone == null || tone.degree <= 0)))) {
                return null;
            }
            long delta = (long)targetTick - AnchorTick;
            if (Regions.Any(region => region.position + delta < 0 || region.position + delta + region.duration > int.MaxValue)) {
                return null;
            }
            var result = Regions.Select(region => region.Clone()).ToList();
            foreach (var region in result) {
                region.position = (int)(region.position + delta);
                if (!region.Normalize(project.Is31Edo)) return null;
            }
            return result;
        }
    }
}
