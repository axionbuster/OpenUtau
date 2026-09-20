using System;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Ustx {
    public class ChordHelperTest {
        static UProject Fixture(bool native) {
            var project = Format.Ustx.Create();
            project.Is31Edo = native;
            project.parts.Add(new UVoicePart {
                trackNo = 0,
                position = 960,
                duration = 480,
            });
            return project;
        }

        static string SaveText(UProject project) {
            project.BeforeSave();
            try { return Ustx31.Serialize(project); }
            finally { project.AfterSave(); }
        }

        [Fact]
        public void PresetsUseExactTemperamentArithmetic() {
            Assert.Equal(new[] { 0, 10, 18 }, ChordHelperTheory.CreatePreset("Major").Select(t => t.Offset(true)));
            Assert.Equal(new[] { 0, 8, 18 }, ChordHelperTheory.CreatePreset("Minor").Select(t => t.Offset(true)));
            Assert.Equal(new[] { 0, 8, 16, 24 }, ChordHelperTheory.CreatePreset("Diminished seventh").Select(t => t.Offset(true)));
            Assert.Equal(new[] { 0, 10, 20 }, ChordHelperTheory.CreatePreset("Augmented").Select(t => t.Offset(true)));
            Assert.Equal(new[] { 0, 10, 18, 23 }, ChordHelperTheory.CreatePreset("Sixth").Select(t => t.Offset(true)));
            Assert.Equal(new[] { 0, 10, 18, 26 }, ChordHelperTheory.CreatePreset("Dominant seventh").Select(t => t.Offset(true)));
            Assert.Equal(new[] { 0, 4, 7 }, ChordHelperTheory.CreatePreset("Major").Select(t => t.Offset(false)));
            Assert.Equal(new[] { 0, 3, 7 }, ChordHelperTheory.CreatePreset("Minor").Select(t => t.Offset(false)));
            Assert.Equal(25, ChordHelperTheory.CreatePreset("Harmonic seventh").Last().Offset(true));
            Assert.Equal("♯6", ChordHelperTheory.CreatePreset("Harmonic seventh").Last().Label);
            Assert.Equal("♯4", ChordHelperTheory.CanonicalInterval(15, true).Label);
            Assert.Equal("♭5", ChordHelperTheory.CanonicalInterval(16, true).Label);
        }

        [Fact]
        public void RecognitionIsExactAndCustomIsHonest() {
            var major = ChordHelperTheory.CreatePreset("Major");
            Assert.Equal("Major", ChordHelperTheory.QualityName(major, true));
            major.Add(new UChordInterval(4, 1));
            Assert.Equal("Custom (1, 3, ♯4, 5)", ChordHelperTheory.QualityName(major, true));
            major.RemoveAll(t => t.degree == 1);
            Assert.Equal("Custom (3, ♯4, 5)", ChordHelperTheory.QualityName(major, true));
        }

        [Fact]
        public void ChordNamesUseExactQualitiesEllipsesAndSpelledBass() {
            var helper = new UChordHelper {
                root = 0,
                tones = ChordHelperTheory.CreatePreset("Major"),
                bass = new UChordInterval(3),
            };
            Assert.Equal("C/E", ChordHelperTheory.ChordName(helper, false, 0));
            Assert.Equal("C/E", ChordHelperTheory.ChordName(helper, true, 0));

            helper.tones.Add(new UChordInterval(4, 1));
            Assert.Equal("C.../E", ChordHelperTheory.ChordName(helper, false, 0));
            Assert.Equal("C.../E", ChordHelperTheory.ChordName(helper, true, 0));

            helper.tones = ChordHelperTheory.CreatePreset("Diminished");
            helper.bass = new UChordInterval(5, -1);
            Assert.Equal("Cdim/G♭", ChordHelperTheory.ChordName(helper, true, 0));
            helper.bass = null;
            Assert.Equal("Cdim", ChordHelperTheory.ChordName(helper, false, 0));

            helper.tones = ChordHelperTheory.CreatePreset("Unison");
            Assert.Equal("C (unison)", ChordHelperTheory.ChordName(helper, false, 0));
            helper.tones = ChordHelperTheory.CreatePreset("Fifth");
            Assert.Equal("C5", ChordHelperTheory.ChordName(helper, false, 0));
        }

        [Fact]
        public void InversionDoesNotChangeMembershipAndResetsWhenRemoved() {
            var helper = new UChordHelper {
                tones = ChordHelperTheory.CreatePreset("Major"),
                bass = new UChordInterval(3),
            };
            var before = helper.tones.Select(t => t.Offset(true)).ToArray();
            helper.Normalize(true);
            Assert.Equal(before, helper.tones.Select(t => t.Offset(true)));
            helper.tones.RemoveAll(t => t.degree == 3);
            helper.Normalize(true);
            Assert.Null(helper.bass);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RoundTripCloneAndSilenceSeparation(bool native) {
            var project = Fixture(native);
            var part = project.parts.OfType<UVoicePart>().Single();
            part.chordHelpers.Add(new UChordHelper {
                position = 240,
                duration = 960,
                root = native ? 15 : 6,
                tones = new() { new(1), new(4, 1), new(7, -1) },
                bass = new UChordInterval(4, 1),
                highlightRoot = false,
                color = "#C04080",
            });
            var clone = (UVoicePart)part.Clone();
            Assert.Empty(clone.notes);
            Assert.Empty(clone.phonemes);
            Assert.Single(clone.chordHelpers);
            Assert.NotSame(part.chordHelpers[0], clone.chordHelpers[0]);

            var loaded = Ustx31.Deserialize(SaveText(project));
            loaded.AfterLoad();
            var actual = loaded.parts.OfType<UVoicePart>().Single().chordHelpers.Single();
            Assert.Equal(240, actual.position);
            Assert.Equal(960, actual.duration);
            Assert.Equal(native ? 15 : 6, actual.root);
            Assert.Equal("♯4", actual.tones[1].Label);
            Assert.False(actual.highlightRoot);
            Assert.Empty(loaded.parts.OfType<UVoicePart>().Single().notes);
        }

        [Fact]
        public void PartTimingIncludesHelpersAndMovesAbsoluteTimeOnce() {
            var project = Fixture(true);
            var part = project.parts.OfType<UVoicePart>().Single();
            var helper = new UChordHelper { position = 120, duration = 960 };
            part.chordHelpers.Add(helper);
            Assert.True(part.GetMinDurTickForNoteEdit(project, 1) >= helper.End);
            int absolute = part.position + helper.position;
            var move = new MovePartCommand(project, part, part.position + 480, 0);
            move.Execute();
            Assert.Equal(absolute + 480, part.position + helper.position);
            Assert.Equal(120, helper.position);
            move.Unexecute();
            Assert.Equal(absolute, part.position + helper.position);
        }

        [Fact]
        public void CommandsUndoAddChangeAndRemoveWithoutTouchingNotes() {
            var part = Fixture(false).parts.OfType<UVoicePart>().Single();
            var helper = new UChordHelper();
            var add = new AddChordHelperCommand(part, helper);
            add.Execute();
            Assert.Single(part.chordHelpers);
            var changed = helper.Clone();
            changed.position = 240;
            changed.tones = ChordHelperTheory.CreatePreset("Unison");
            var change = new ChangeChordHelperCommand(part, helper, changed);
            change.Execute();
            Assert.Equal(240, helper.position);
            Assert.Single(helper.tones);
            change.Unexecute();
            Assert.Equal(0, helper.position);
            Assert.Equal(3, helper.tones.Count);
            var remove = new RemoveChordHelperCommand(part, helper);
            remove.Execute();
            Assert.Empty(part.chordHelpers);
            remove.Unexecute();
            Assert.Same(helper, Assert.Single(part.chordHelpers));
            Assert.Empty(part.notes);
        }

        [Fact]
        public void ConversionKeepsIntervalIdentityInsteadOfRoundingMask() {
            var ordinary = Fixture(false);
            var helper = new UChordHelper {
                root = 6,
                tones = ChordHelperTheory.CreatePreset("Diminished seventh"),
            };
            ordinary.parts.OfType<UVoicePart>().Single().chordHelpers.Add(helper);
            var native = Ustx31.ConvertCopy(ordinary, true);
            var converted = native.parts.OfType<UVoicePart>().Single().chordHelpers.Single();
            Assert.Equal(16, converted.root);
            Assert.Equal(new[] { 0, 8, 16, 24 }, converted.tones.Select(t => t.Offset(true)));
            Assert.Equal("Diminished seventh", ChordHelperTheory.QualityName(converted.tones, true));
            var back = Ustx31.ConvertCopy(native, false);
            var roundTripped = back.parts.OfType<UVoicePart>().Single().chordHelpers.Single();
            Assert.Equal(new[] { "1", "♭3", "♭5", "♭♭7" }, roundTripped.tones.Select(t => t.Label));
            Assert.Equal(new[] { 0, 3, 6, 9 }, roundTripped.tones.Select(t => t.Offset(false)));
        }
    }
}
