using System.Linq;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class ChordRegionTest {
        static UChordRegion Loop() => new UChordRegion {
            position = 0, sourceDuration = 1920, duration = 4800,
            chordHelpers = new() {
                new UChordHelper { position = 0, duration = 480 },
                new UChordHelper { position = 960, duration = 480 },
            },
        };

        [Fact]
        public void ExpandsFullCyclesAndPartialFinalCycle() {
            var occurrences = ChordRegionExpander.Enumerate(Loop(), 0, 6000).ToArray();
            Assert.Equal(new[] { 0, 960, 1920, 2880, 3840 }, occurrences.Select(item => item.StartTick));
            Assert.Equal(new[] { 480, 1440, 2400, 3360, 4320 }, occurrences.Select(item => item.EndTick));
            Assert.DoesNotContain(occurrences, item => item.StartTick == 4800);
        }

        [Fact]
        public void ViewportEnumerationReturnsOnlyIntersectingOccurrences() {
            var occurrences = ChordRegionExpander.Enumerate(Loop(), 2000, 3000).ToArray();
            Assert.Equal(new[] { 1920, 2880 }, occurrences.Select(item => item.StartTick));
        }

        [Fact]
        public void ShortenThenExpandDoesNotLoseSourceMaterial() {
            var region = Loop();
            region.duration = 500;
            Assert.Single(ChordRegionExpander.Enumerate(region, 0, 5000));
            Assert.Equal(2, region.chordHelpers.Count);
            region.duration = 4800;
            Assert.Equal(5, ChordRegionExpander.Enumerate(region, 0, 5000).Count());
        }

        [Fact]
        public void BreakMaterializesIndependentRegionsAndClipsFinal() {
            var loop = Loop();
            Assert.True(ChordRegionExpander.TryBreak(loop, out var pieces));
            Assert.Equal(new[] { 1920, 1920, 960 }, pieces.Select(piece => piece.duration));
            Assert.Equal(new[] { 0, 1920, 3840 }, pieces.Select(piece => piece.position));
            Assert.Equal(new[] { 2, 2, 1 }, pieces.Select(piece => piece.chordHelpers.Count));
            pieces[0].chordHelpers[0].root = 7;
            Assert.Equal(0, pieces[1].chordHelpers[0].root);
            Assert.Equal(0, loop.chordHelpers[0].root);
            var before = ChordRegionExpander.Enumerate(loop, 0, 5000).Select(x => (x.StartTick, x.EndTick));
            var after = pieces.SelectMany(piece => ChordRegionExpander.Enumerate(piece, 0, 5000))
                .Select(x => (x.StartTick, x.EndTick));
            Assert.Equal(before, after);
        }

        [Fact]
        public void BreakRejectsHugeLoopWithoutOutput() {
            var loop = Loop();
            loop.sourceDuration = 1;
            loop.duration = ChordRegionExpander.MaxBreakRegions + 1;
            Assert.False(ChordRegionExpander.TryBreak(loop, out var pieces));
            Assert.Empty(pieces);
            Assert.Equal(2, loop.chordHelpers.Count);
        }

        [Fact]
        public void ClipboardDeepClonesLoopWithNewIdentity() {
            var project = new UProject();
            var source = Loop();
            var payload = new ChordClipboardPayload(new[] { source }, false, null);
            var pasted = Assert.Single(payload.CloneRegionsForPaste(project, 1440)!);
            Assert.Equal(1440, pasted.position);
            Assert.Equal(source.sourceDuration, pasted.sourceDuration);
            Assert.Equal(source.duration, pasted.duration);
            Assert.NotEqual(source.id, pasted.id);
            pasted.chordHelpers[0].root = 4;
            Assert.Equal(0, source.chordHelpers[0].root);
        }

        [Fact]
        public void CreateFromSelectionPreservesPhraseSilenceAndRelativeTiming() {
            var helpers = new[] {
                new UChordHelper { position = 1200, duration = 240 },
                new UChordHelper { position = 1800, duration = 300 },
                new UChordHelper { position = 3000, duration = 240 },
            };
            var region = ChordRegionExpander.CreateFromFreeHelpers(helpers, 960, 2400, 480)!;
            Assert.Equal(960, region.position);
            Assert.Equal(1440, region.sourceDuration);
            Assert.Equal(new[] { 240, 840 }, region.chordHelpers.Select(helper => helper.position));
            Assert.Equal(new[] { 240, 300 }, region.chordHelpers.Select(helper => helper.duration));
        }

        [Fact]
        public void GroupCommandsUndoAndRedoWithoutAliasing() {
            var part = new UVoicePart();
            var first = new UChordHelper { position = 120, duration = 240 };
            var second = new UChordHelper { position = 720, duration = 480 };
            part.chordHelpers.AddRange(new[] { first, second });
            var region = ChordRegionExpander.CreateFromFreeHelpers(part.chordHelpers, 0, 1440, 480)!;
            var removeFirst = new OpenUtau.Core.RemoveChordHelperCommand(part, first);
            var removeSecond = new OpenUtau.Core.RemoveChordHelperCommand(part, second);
            var add = new OpenUtau.Core.AddChordRegionCommand(part, region);
            removeFirst.Execute(); removeSecond.Execute(); add.Execute();
            Assert.Empty(part.chordHelpers);
            Assert.Same(region, Assert.Single(part.chordRegions));
            add.Unexecute(); removeSecond.Unexecute(); removeFirst.Unexecute();
            Assert.Equal(new[] { first, second }, part.chordHelpers);
            Assert.Empty(part.chordRegions);
            removeFirst.Execute(); removeSecond.Execute(); add.Execute();
            Assert.Equal(2, part.chordRegions.Single().chordHelpers.Count);
        }

        [Fact]
        public void ActiveEmptyRegionCanAccumulateIndependentSourceHelpers() {
            var region = new UChordRegion { position = 1440, sourceDuration = 1920, duration = 3840 };
            var first = new UChordHelper { position = 0, duration = 480 };
            var second = new UChordHelper { position = 960, duration = 480 };
            region.chordHelpers.Add(first);
            region.chordHelpers.Add(second);
            Assert.Equal(new[] { 1440, 2400, 3360, 4320 },
                ChordRegionExpander.Enumerate(region, 0, 6000).Select(item => item.StartTick));
        }
    }
}
