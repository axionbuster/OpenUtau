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
            Assert.Equal("Custom (1, 3, ♯11, 5)", ChordHelperTheory.QualityName(major, true));
            major.RemoveAll(t => t.degree == 1);
            Assert.Equal("Custom (3, ♯11, 5)", ChordHelperTheory.QualityName(major, true));
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
        public void CompoundDegreesFollowChordContextWithoutChangingPitchClass() {
            var major = ChordHelperTheory.CreatePreset("Major");
            Assert.Equal("9", ChordHelperTheory.DisplayInterval(new UChordInterval(2), major, false).Label);
            Assert.Equal(2, Edo31.Mod(new UChordInterval(2).Offset(false), 12));
            Assert.Equal(2, Edo31.Mod(new UChordInterval(9).Offset(false), 12));
            Assert.Equal(5, Edo31.Mod(new UChordInterval(2).Offset(true), 31));
            Assert.Equal(5, Edo31.Mod(new UChordInterval(9).Offset(true), 31));
            Assert.Equal("♯11", ChordHelperTheory.DisplayInterval(new UChordInterval(4, 1), major, true).Label);

            var sus2 = ChordHelperTheory.CreatePreset("Sus2");
            var sus4 = ChordHelperTheory.CreatePreset("Sus4");
            Assert.Equal("2", ChordHelperTheory.DisplayInterval(sus2[1], sus2, false).Label);
            Assert.Equal("4", ChordHelperTheory.DisplayInterval(sus4[1], sus4, false).Label);

            var sixth = ChordHelperTheory.CreatePreset("Sixth");
            Assert.Equal("6", ChordHelperTheory.DisplayInterval(sixth[^1], sixth, false).Label);
            var dominant = ChordHelperTheory.CreatePreset("Dominant seventh");
            Assert.Equal("13", ChordHelperTheory.DisplayInterval(new UChordInterval(6), dominant, false).Label);

            var diminished = ChordHelperTheory.CreatePreset("Diminished");
            Assert.Equal("♭5", ChordHelperTheory.DisplayInterval(diminished[^1], diminished, true).Label);
            var harmonic = ChordHelperTheory.CreatePreset("Harmonic seventh");
            Assert.Equal("♯6", ChordHelperTheory.DisplayInterval(harmonic[^1], harmonic, true).Label);
            Assert.Equal("♭♭8", ChordHelperTheory.DisplayInterval(
                ChordHelperTheory.CanonicalInterval(27, true), major, true).Label);
            Assert.Equal("♭8", ChordHelperTheory.DisplayInterval(
                ChordHelperTheory.CanonicalInterval(29, true), major, true).Label);
        }

        [Fact]
        public void ExtendedPresetsKeepExplicitDegreeIdentityAndNames() {
            var add9 = new UChordHelper { tones = ChordHelperTheory.CreatePreset("Add ninth") };
            Assert.Equal(new[] { "1", "3", "5", "9" }, add9.tones.Select(tone => tone.Label));
            Assert.Equal("Add ninth", ChordHelperTheory.QualityName(add9.tones, false));
            Assert.Equal("Cadd9", ChordHelperTheory.ChordName(add9, false, 0));

            var ninth = new UChordHelper { tones = ChordHelperTheory.CreatePreset("Dominant ninth") };
            Assert.Equal("C9", ChordHelperTheory.ChordName(ninth, false, 0));
            Assert.Equal("C9", ChordHelperTheory.ChordName(ninth, true, 0));
            var eleventh = new UChordHelper { tones = ChordHelperTheory.CreatePreset("Dominant eleventh") };
            Assert.Equal("C11", ChordHelperTheory.ChordName(eleventh, false, 0));
            var thirteenth = new UChordHelper { tones = ChordHelperTheory.CreatePreset("Dominant thirteenth") };
            Assert.Equal("C13", ChordHelperTheory.ChordName(thirteenth, false, 0));
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
            var part = project.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart);
            part.chordHelpers.Add(new UChordHelper {
                position = 240,
                duration = 960,
                root = native ? 15 : 6,
                rootTone = native ? 170 : 66,
                tones = new() { new(1), new(4, 1), new(7, -1) },
                bass = new UChordInterval(4, 1),
                highlightRoot = false,
                mute = true,
                color = "#C04080",
            });
            var clone = (UVoicePart)part.Clone();
            Assert.Empty(clone.notes);
            Assert.Empty(clone.phonemes);
            Assert.Single(clone.chordHelpers);
            Assert.NotSame(part.chordHelpers[0], clone.chordHelpers[0]);

            var loaded = Ustx31.Deserialize(SaveText(project));
            loaded.AfterLoad();
            var actual = loaded.ChordsPart.chordHelpers.Single();
            Assert.Equal(1200, actual.position);
            Assert.Equal(960, actual.duration);
            Assert.Equal(native ? 15 : 6, actual.root);
            Assert.Equal(native ? 170 : 66, actual.rootTone);
            Assert.Equal("♯4", actual.tones[1].Label);
            Assert.False(actual.highlightRoot);
            Assert.True(actual.mute);
            Assert.Empty(loaded.ChordsPart.notes);
        }

        [Fact]
        public void PartTimingIncludesHelpersAndMovesAbsoluteTimeOnce() {
            var project = Fixture(true);
            var part = project.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart);
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
            var part = Fixture(false).parts.OfType<UVoicePart>().Single(part => !part.IsChordPart);
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
                rootTone = 66,
                tones = ChordHelperTheory.CreatePreset("Diminished seventh"),
            };
            ordinary.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).chordHelpers.Add(helper);
            var native = Ustx31.ConvertCopy(ordinary, true);
            var converted = native.ChordsPart.chordHelpers.Single();
            Assert.Equal(16, converted.root);
            Assert.Equal(171, converted.rootTone);
            Assert.Equal(new[] { 0, 8, 16, 24 }, converted.tones.Select(t => t.Offset(true)));
            Assert.Equal("Diminished seventh", ChordHelperTheory.QualityName(converted.tones, true));
            var back = Ustx31.ConvertCopy(native, false);
            var roundTripped = back.ChordsPart.chordHelpers.Single();
            Assert.Equal(66, roundTripped.rootTone);
            Assert.Equal(new[] { "1", "♭3", "♭5", "♭♭7" }, roundTripped.tones.Select(t => t.Label));
            Assert.Equal(new[] { 0, 3, 6, 9 }, roundTripped.tones.Select(t => t.Offset(false)));
        }
    }
}
