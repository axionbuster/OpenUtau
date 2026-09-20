using OpenUtau.Core.Ustx;
using System.Linq;

namespace OpenUtau.Core {
    public abstract class ChordHelperCommand : UCommand {
        public readonly UVoicePart Part;
        public readonly UChordHelper Helper;
        protected readonly UChordRegion? Region;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            SkipPhonemizer = true,
            SkipPhoneme = true,
            Part = Part,
        };
        public override Pipeline.ImpactSet Impact => Pipeline.ImpactSet.None;

        protected ChordHelperCommand(UVoicePart part, UChordHelper helper, UChordRegion? region = null) {
            Part = part;
            Helper = helper;
            Region = region ?? part.chordRegions.FirstOrDefault(item => item.chordHelpers.Contains(helper));
        }
        protected System.Collections.Generic.List<UChordHelper> Helpers => Region?.chordHelpers ?? Part.chordHelpers;
    }

    public sealed class AddChordHelperCommand : ChordHelperCommand {
        public AddChordHelperCommand(UVoicePart part, UChordHelper helper, UChordRegion? region = null) : base(part, helper, region) { }
        public override void Execute() => Helpers.Add(Helper);
        public override void Unexecute() => Helpers.Remove(Helper);
        public override string ToString() => "Add chord helper";
    }

    public sealed class RemoveChordHelperCommand : ChordHelperCommand {
        readonly int index;
        public RemoveChordHelperCommand(UVoicePart part, UChordHelper helper) : base(part, helper) {
            index = Helpers.IndexOf(helper);
        }
        public override void Execute() => Helpers.Remove(Helper);
        public override void Unexecute() => Helpers.Insert(
            index < 0 ? Helpers.Count : System.Math.Min(index, Helpers.Count), Helper);
        public override string ToString() => "Remove chord helper";
    }

    /// <summary>Applies a complete snapshot while preserving selection identity.</summary>
    public sealed class ChangeChordHelperCommand : ChordHelperCommand {
        readonly UChordHelper before;
        readonly UChordHelper after;
        public ChangeChordHelperCommand(UVoicePart part, UChordHelper helper, UChordHelper changed)
            : base(part, helper) {
            before = helper.Clone();
            after = changed.Clone();
        }
        public override void Execute() => Helper.CopyFrom(after);
        public override void Unexecute() => Helper.CopyFrom(before);
        public override string ToString() => "Change chord helper";
    }

    public abstract class ChordRegionCommand : UCommand {
        public readonly UVoicePart Part;
        public readonly UChordRegion Region;
        public override ValidateOptions ValidateOptions => new ValidateOptions { SkipTiming = true, SkipPhonemizer = true, SkipPhoneme = true, Part = Part };
        public override Pipeline.ImpactSet Impact => Pipeline.ImpactSet.None;
        protected ChordRegionCommand(UVoicePart part, UChordRegion region) { Part = part; Region = region; }
    }
    public sealed class AddChordRegionCommand : ChordRegionCommand {
        public AddChordRegionCommand(UVoicePart part, UChordRegion region) : base(part, region) { }
        public override void Execute() => Part.chordRegions.Add(Region);
        public override void Unexecute() => Part.chordRegions.Remove(Region);
        public override string ToString() => "Add chord region";
    }
    public sealed class RemoveChordRegionCommand : ChordRegionCommand {
        readonly int index;
        public RemoveChordRegionCommand(UVoicePart part, UChordRegion region) : base(part, region) { index = part.chordRegions.IndexOf(region); }
        public override void Execute() => Part.chordRegions.Remove(Region);
        public override void Unexecute() => Part.chordRegions.Insert(System.Math.Clamp(index, 0, Part.chordRegions.Count), Region);
        public override string ToString() => "Remove chord region";
    }
    public sealed class ChangeChordRegionCommand : ChordRegionCommand {
        readonly UChordRegion before, after;
        public ChangeChordRegionCommand(UVoicePart part, UChordRegion region, UChordRegion changed) : base(part, region) {
            before = region.Clone(false); after = changed.Clone(false);
        }
        public override void Execute() => Region.CopyFrom(after);
        public override void Unexecute() => Region.CopyFrom(before);
        public override string ToString() => "Change chord region";
    }
}
