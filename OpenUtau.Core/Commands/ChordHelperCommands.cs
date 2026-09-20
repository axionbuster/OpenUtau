using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public abstract class ChordHelperCommand : UCommand {
        public readonly UVoicePart Part;
        public readonly UChordHelper Helper;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            SkipPhonemizer = true,
            SkipPhoneme = true,
            Part = Part,
        };
        public override Pipeline.ImpactSet Impact => Pipeline.ImpactSet.None;

        protected ChordHelperCommand(UVoicePart part, UChordHelper helper) {
            Part = part;
            Helper = helper;
        }
    }

    public sealed class AddChordHelperCommand : ChordHelperCommand {
        public AddChordHelperCommand(UVoicePart part, UChordHelper helper) : base(part, helper) { }
        public override void Execute() => Part.chordHelpers.Add(Helper);
        public override void Unexecute() => Part.chordHelpers.Remove(Helper);
        public override string ToString() => "Add chord helper";
    }

    public sealed class RemoveChordHelperCommand : ChordHelperCommand {
        readonly int index;
        public RemoveChordHelperCommand(UVoicePart part, UChordHelper helper) : base(part, helper) {
            index = part.chordHelpers.IndexOf(helper);
        }
        public override void Execute() => Part.chordHelpers.Remove(Helper);
        public override void Unexecute() => Part.chordHelpers.Insert(
            index < 0 ? Part.chordHelpers.Count : System.Math.Min(index, Part.chordHelpers.Count), Helper);
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
}
