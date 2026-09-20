using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core {
    public class ChordClipboardPayloadTest {
        [Fact]
        public void DeepClonesChordIdentityAndPreservesSpacing() {
            var first = new UChordHelper {
                position = 240, duration = 360, root = 3, rootTone = 63,
                tones = [new UChordInterval(1), new UChordInterval(3, -1)],
                bass = new UChordInterval(3, -1), highlightRoot = false, mute = true,
                color = "#123456",
            };
            var second = first.Clone();
            second.position = 960;
            var payload = new ChordClipboardPayload([first, second], false, null);
            first.tones[0].degree = 7;

            var project = Ustx.Create();
            var pasted = payload.CloneForPaste(project, 1920)!;

            Assert.Equal([1920, 2640], pasted.Select(helper => helper.position));
            Assert.Equal(360, pasted[0].duration);
            Assert.Equal(1, pasted[0].tones[0].degree);
            Assert.Equal(new UChordInterval(3, -1), pasted[0].bass);
            Assert.False(pasted[0].highlightRoot);
            Assert.True(pasted[0].mute);
            Assert.Equal("#123456", pasted[0].color);
            Assert.NotSame(payload.Helpers[0].tones[0], pasted[0].tones[0]);
        }

        [Fact]
        public void RejectsTuningMismatchAndMalformedPayloadAtomically() {
            var project = Ustx.Create();
            project.Is31Edo = true;
            var mismatch = new ChordClipboardPayload([new UChordHelper()], false, null);
            Assert.Null(mismatch.CloneForPaste(project, 0));

            var malformed = new UChordHelper { duration = 0 };
            var payload = new ChordClipboardPayload([malformed], true, project.PitchReference31);
            Assert.Null(payload.CloneForPaste(project, 0));
        }
    }
}
