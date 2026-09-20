using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenUtau.Core.Format;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Ustx {
    [Collection(RenderSingletonCollection.Name)]
    public class Edo31Test {
        static UProject Fixture(bool native) {
            var project = Format.Ustx.Create();
            project.Is31Edo = native;
            var part = new UVoicePart { trackNo = project.tracks.Single(track => !track.IsChordsTrack).TrackNo, position = 0 };
            project.parts.Add(part);
            var note = project.CreateGridNote(native ? 156 : 60, 0, 480);
            note.tuning = 7;
            part.notes.Add(note);
            return project;
        }
        static string SaveText(UProject project) {
            project.BeforeSave();
            try { return Ustx31.Serialize(project); }
            finally { project.AfterSave(); }
        }
        [Fact]
        public void AllSpellingsRoundTripAndColorsFollowTonic() {
            Assert.Equal(2, new Preferences.SerializablePreferences().PreferredKey31Fifths);
            Assert.Equal(96, new Preferences.SerializablePreferences().PianoRollKeyboardWidth);
            for (int key = -15; key <= 15; key++) {
                Assert.Equal(31, Enumerable.Range(0, 31).Select(s => Edo31.FifthsForStep(s, key)).Distinct().Count());
                for (int step = 0; step < Edo31.MaxStep; step++) {
                    Assert.Equal(step, Edo31.ParseName(Edo31.Name(step, key)));
                    Assert.InRange(Edo31.ScaleColorIndex(step, key), 0, 30);
                    Assert.Equal(Edo31.ScaleColorIndex(step, 0), Edo31.ScaleColorIndex(step + key * 18, key));
                }
            }
            Assert.Equal(31, Enumerable.Range(0, 31).Select(s => Edo31.ScaleColorIndex(s, 0)).Distinct().Count());
            string[] degreeLabels = {
                "1", "♭♭2", "♯1", "♭2", "♯♯1", "2", "♭♭3", "♯2",
                "♭3", "♭♭4", "3", "♭4", "♯3", "4", "♭♭5", "♯4",
                "♭5", "♯♯4", "5", "♭♭6", "♯5", "♭6", "♯♯5", "6",
                "♭♭7", "♯6", "♭7", "♭♭1", "7", "♭1", "♯7",
            };
            for (int step = 0; step < Edo31.Divisions; step++) {
                Assert.Equal(degreeLabels[step], Edo31.ScaleDegreeLabel(step, 0));
                Assert.Equal(degreeLabels[step], Edo31.ScaleDegreeLabel(step + 36, 2));
            }
            Assert.Equal(Edo31.SeptimalAugmentedSixth, (int)Math.Round(31 * Math.Log2(7.0 / 4)));
            Assert.True(Edo31.IsDegreeInSelectedScales(10, major: true, minor: false));
            Assert.False(Edo31.IsDegreeInSelectedScales(10, major: false, minor: true));
            Assert.True(Edo31.IsDegreeInSelectedScales(8, major: false, minor: true));
            Assert.False(Edo31.IsDegreeInSelectedScales(8, major: true, minor: false));
            Assert.True(Edo31.IsDegreeInSelectedScales(13, major: true, minor: true));
            Assert.False(Edo31.IsDegreeInSelectedScales(Edo31.SeptimalAugmentedSixth, major: true, minor: true));
            Assert.False(Edo31.IsDegreeInSelectedScales(0, major: false, minor: false));
            for (int key = -15; key <= 15; key++) {
                Assert.Equal("♯6", Edo31.ScaleDegreeLabel(
                    key * 18 + Edo31.SeptimalAugmentedSixth, key));
            }
            Assert.Equal("♭3", Edo31.ScaleDegreeLabel(13, 2));
            Assert.Equal("C4", Edo31.Name(155, 2));
            Assert.NotEqual(Edo31.ParseName("C#4"), Edo31.ParseName("Db4"));
            Assert.Equal(Edo31.ParseName("F♭♭4"), Edo31.ParseName("F𝄫4"));
            Assert.Equal(Edo31.ParseName("F♯♯4"), Edo31.ParseName("F𝄪4"));
        }
        [Theory]
        [InlineData(-8, "F♭")]
        [InlineData(-15, "F♭♭")]
        [InlineData(-22, "F♭♭♭")]
        [InlineData(-29, "F♭♭♭♭")]
        [InlineData(-30, "B♭♭♭♭♭")]
        [InlineData(13, "F♯♯")]
        [InlineData(20, "F♯♯♯")]
        public void FifthNamesUsePairedUiAccidentalGlyphs(int fifths, string expected) {
            Assert.Equal(expected, Edo31.FifthName(fifths));
        }
        [Fact]
        public void NativeRoundTripPreservesExactStepAndCentsWithoutPreferredKey() {
            var text = SaveText(Fixture(true));
            Assert.StartsWith("format: " + Ustx31.FormatId, text);
            Assert.DoesNotContain("ustx_version:", text);
            Assert.DoesNotContain("key:", text);
            Assert.DoesNotContain("preferred", text);
            Assert.Contains("features:", text);
            Assert.Contains("pitch-reference-v1", text);
            Assert.Contains("pitch_reference:", text);
            Assert.Contains("frequency: 440", text);
            var project = Ustx31.Deserialize(text);
            var note = Assert.Single(project.voiceParts.Single(part => !part.IsChordPart).notes);
            Assert.True(project.Is31Edo);
            Assert.Equal(156, note.tone31);
            Assert.Equal(7, note.tuning);
            Assert.InRange(Math.Abs(note.AdjustedTone - (156 * 12.0 / 31 + .07)), 0, 0.00001);
            Assert.Equal(440, project.ToneToFrequency(Edo31.A4Step * Edo31.StepTone), 10);
        }
        [Fact]
        public void OrdinarySerializationIsIdenticalToUpstreamSerializer() {
            var project = Fixture(false);
            project.BeforeSave();
            var upstream = Yaml.DefaultSerializer.Serialize(project);
            var actual = Ustx31.Serialize(project);
            Assert.Equal(upstream, actual);
            Assert.DoesNotContain("tone31", actual);
            Assert.DoesNotContain("format_revision", actual);
            Assert.DoesNotContain("features", actual);
            var loaded = Ustx31.Deserialize(actual);
            Assert.False(loaded.Is31Edo);
            Assert.Equal(7, loaded.voiceParts.Single(part => !part.IsChordPart).notes.First().tuning);
        }
        [Theory]
        [InlineData("format_revision: 2", "format_revision: 3")]
        [InlineData("edo31-v1", "unknown-v1")]
        [InlineData("tone31: 156", "tone31: -1")]
        [InlineData("tone31: 156", "tone31: 341")]
        [InlineData("tone31: 156", "unrecognized: 156")]
        [InlineData("frequency: 440", "frequency: -1")]
        [InlineData("mode: a4-frequency", "mode: unknown")]
        public void RejectsUnsupportedOrInvalidNativeData(string before, string after) {
            var text = SaveText(Fixture(true));
            Assert.Contains(before, text);
            Assert.ThrowsAny<Exception>(() => Ustx31.Deserialize(text.Replace(before, after)));
        }
        [Fact]
        public void OrdinaryFileCannotSmuggleNativeNotes() {
            string text = SaveText(Fixture(false)).Replace("tone: 60", "tone: 60\n    tone31: 155");
            Assert.ThrowsAny<Exception>(() => Ustx31.Deserialize(text));
        }
        [Fact]
        public void PitchReferenceUsesNativeAnchorsAndRoundTrips() {
            var project = Fixture(true);
            Assert.Equal(440, project.PitchReference31.EffectiveA4Frequency, 10);
            Assert.Equal(440, project.ToneToFrequency(Edo31.A4Step * Edo31.StepTone), 10);

            project.PitchReference31 = new Edo31PitchReference {
                Mode = Edo31PitchReferenceMode.TwelveTetNote,
                TwelveTetPitchClass = 0,
            };
            Assert.Equal(MusicMath.ToneToFreq(60), project.ToneToFrequency(155 * Edo31.StepTone), 10);
            Assert.Equal(437.5473070250114, project.PitchReference31.EffectiveA4Frequency, 10);

            project.PitchReference31.TwelveTetPitchClass = 2;
            Assert.Equal(MusicMath.ToneToFreq(62), project.ToneToFrequency((155 + 5) * Edo31.StepTone), 10);
            var loaded = Ustx31.Deserialize(SaveText(project));
            Assert.Equal(Edo31PitchReferenceMode.TwelveTetNote, loaded.PitchReference31.Mode);
            Assert.Equal(2, loaded.PitchReference31.TwelveTetPitchClass);
            Assert.Equal(project.PitchReference31.EffectiveA4Frequency,
                loaded.PitchReference31.EffectiveA4Frequency, 10);
        }
        [Fact]
        public void RevisionOneMigratesToItsOriginalCReference() {
            string text = SaveText(Fixture(true))
                .Replace("format_revision: 2", "format_revision: 1")
                .Replace("- pitch-reference-v1\n", "");
            text = Regex.Replace(text,
                @"  pitch_reference:\n    mode: a4-frequency\n    frequency: 440\n", "");
            var project = Ustx31.Deserialize(text);
            Assert.Equal(Edo31PitchReferenceMode.TwelveTetNote, project.PitchReference31.Mode);
            Assert.Equal(0, project.PitchReference31.TwelveTetPitchClass);
            Assert.Equal(MusicMath.ToneToFreq(60), project.ToneToFrequency(155 * Edo31.StepTone), 10);
        }
        [Fact]
        public void PitchReferenceCommandIsUndoable() {
            var project = Fixture(true);
            var command = new PitchReference31Command(project, new Edo31PitchReference {
                Mode = Edo31PitchReferenceMode.A4Frequency,
                A4Frequency = 442,
            });
            command.Execute();
            Assert.Equal(442, project.ToneToFrequency(Edo31.A4Step * Edo31.StepTone), 10);
            command.Unexecute();
            Assert.Equal(440, project.ToneToFrequency(Edo31.A4Step * Edo31.StepTone), 10);
        }
        [Fact]
        public void FormatDetectorUsesNativeContentAndLoaderRestoresMode() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ustx31");
            try {
                File.WriteAllText(path, SaveText(Fixture(true)));
                Assert.Equal(ProjectFormats.Ustx, Formats.DetectProjectFormat(path));
                var loaded = Format.Ustx.Load(path);
                Assert.True(loaded.Is31Edo);
                Assert.Equal(156, loaded.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).notes.First().tone31);
            } finally { File.Delete(path); }
        }
        [Fact]
        public void MoveUndoAndClonePreserveNativePitch() {
            var project = Fixture(true);
            var part = project.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart);
            var note = part.notes.First();
            var command = new MoveNoteCommand(part, note, 0, 1);
            command.Execute();
            Assert.Equal(157, note.tone31);
            Assert.Equal(157, note.Clone().tone31);
            command.Unexecute();
            Assert.Equal(156, note.tone31);
            Assert.Equal(7, note.tuning);
        }
        [Fact]
        public void InvalidMoveDoesNotRemoveOrPartiallyMoveNotes() {
            var project = Fixture(true);
            var part = project.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart);
            var first = part.notes.First();
            var last = project.CreateGridNote(340, 480, 480);
            part.notes.Add(last);
            var move = new MoveNoteCommand(part, part.notes.ToList(), 120, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => move.Execute());
            Assert.Equal(2, part.notes.Count);
            Assert.Equal(156, first.tone31);
            Assert.Equal(0, first.position);
            Assert.Equal(340, last.tone31);
        }

        [Fact]
        public void ConversionLeavesSourceIntactAndExportsWithinHalfCent() {
            var source = Fixture(true);
            var original = SaveText(source);
            var copy = Ustx31.ConvertCopy(source, false);
            Assert.Equal(original, SaveText(source));
            Assert.False(copy.Is31Edo);
            var note = copy.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).notes.First();
            Assert.Null(note.tone31);
            double sourceFrequency = source.ToneToFrequency(
                source.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).notes.First().PreciseAdjustedTone);
            double copyFrequency = copy.ToneToFrequency(note.PreciseAdjustedTone);
            Assert.InRange(Math.Abs(1200 * Math.Log2(copyFrequency / sourceFrequency)), 0, 0.501);
            Assert.DoesNotContain("features", SaveText(copy));
            var native = Ustx31.ConvertCopy(copy, true);
            Assert.Equal(156, native.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).notes.First().tone31);
            Assert.False(native.Saved);
            Assert.Equal("", native.FilePath);
        }
        [Fact]
        public void SavePathAndImportRejectImplicitConversion() {
            var native = Fixture(true);
            var ordinary = Fixture(false);
            Assert.ThrowsAny<Exception>(() => Ustx31.CheckSavePath("song.ustx", native));
            Assert.ThrowsAny<Exception>(() => Ustx31.CheckSavePath("song.ustx31", ordinary));
            Assert.ThrowsAny<Exception>(() => Formats.ImportTracks(ordinary, new[] { native }));
            Assert.Single(ordinary.tracks.Where(track => !track.IsChordsTrack));
            Assert.True(native.CloneAsTemplate().Is31Edo);
            var differentlyAnchored = Fixture(true);
            differentlyAnchored.PitchReference31 = Edo31PitchReference.LegacyC;
            Assert.ThrowsAny<Exception>(() => Formats.ImportTracks(native, new[] { differentlyAnchored }));
        }

        [Fact]
        public void ImportedProjectWithoutVersionCanConvertWithoutChangingSource() {
            var source = Fixture(false);
            source.ustxVersion = null;
            var native = Ustx31.ConvertCopy(source, true);
            Assert.Null(source.ustxVersion);
            Assert.True(native.Is31Edo);
            Assert.Equal(155, native.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).notes.First().tone31);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SaveAutosaveAndRecoveryRetainMode(bool native) {
            var project = Fixture(native);
            var dir = Path.Combine(Path.GetTempPath(), "ustx-lifecycle-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            var original = Path.Combine(dir, "song" + project.NativeExtension);
            var autosave = Path.Combine(dir, "song-autosave" + project.NativeExtension);
            string prefsPath = PathManager.Inst.PrefsFilePath;
            byte[] prefs = File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null;
            string recoveryPath = Preferences.Default.RecoveryPath;
            bool recovered = DocManager.Inst.Recovered;
            var sink = DocManager.Inst.CommandSink;
            UProject recovery = null;
            DocManager.Inst.CommandSink = cmd => {
                Assert.IsNotType<ErrorMessageNotification>(cmd);
                if (cmd is LoadProjectNotification load) { recovery = load.project; }
            };
            try {
                Format.Ustx.Save(original, project);
                Assert.True(File.Exists(original));
                Assert.True(project.Saved);
                Format.Ustx.AutoSave(autosave, project);
                Assert.True(File.Exists(autosave));
                Assert.Equal(autosave, Preferences.Default.RecoveryPath);
                Formats.RecoveryProject(new[] { autosave });
                Assert.NotNull(recovery);
                Assert.Equal(native, recovery.Is31Edo);
                Assert.Equal(original, recovery.FilePath);
                Assert.Equal(native, recovery.parts.OfType<UVoicePart>().Single(part => !part.IsChordPart).notes.First().tone31.HasValue);
                if (!native) { Assert.DoesNotContain("features:", File.ReadAllText(autosave)); }
            } finally {
                DocManager.Inst.CommandSink = sink;
                DocManager.Inst.Recovered = recovered;
                Preferences.Default.RecoveryPath = recoveryPath;
                if (prefs != null) { File.WriteAllBytes(prefsPath, prefs); }
                else if (File.Exists(prefsPath)) { File.Delete(prefsPath); }
                Directory.Delete(dir, true);
            }
        }
    }
}
