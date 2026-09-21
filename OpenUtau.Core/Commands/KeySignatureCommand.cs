using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public class KeySignatureCommand : ProjectCommand {
        readonly List<UKeySignature>? before;
        readonly List<UKeySignature> after;
        readonly int oldKey;
        public override Pipeline.ImpactSet Impact => Pipeline.ImpactSet.None;
        public override ValidateOptions ValidateOptions => new ValidateOptions { SkipTiming = true, SkipPhonemizer = true, SkipPhoneme = true };
        public KeySignatureCommand(UProject project, IEnumerable<UKeySignature> changes) : base(project) {
            before = project.keySignatures?.Select(change => change.Clone()).ToList();
            after = changes.Select(change => change.Clone()).ToList();
            UKeySignature.Validate(after, project.Is31Edo);
            oldKey = project.key;
        }
        public override void Execute() {
            project.keySignatures = after.Select(change => change.Clone()).ToList();
            if (!project.Is31Edo) project.key = after[0].key;
        }
        public override void Unexecute() {
            project.keySignatures = before?.Select(change => change.Clone()).ToList();
            project.key = oldKey;
        }
        public override string ToString() => "Change project key and mode";
    }
}
