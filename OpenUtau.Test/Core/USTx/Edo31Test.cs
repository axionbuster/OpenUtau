using System;
using System.IO;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Ustx {
    public class Edo31Test {
        static UProject Fixture(bool native) {
            var project = Format.Ustx.Create();
            project.Is31Edo = native;
            var part = new UVoicePart { trackNo = 0, position = 0 };
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
        public void AllSpellingsRoundTripAndColorsStayFixed() {
            Assert.Equal(2, new Preferences.SerializablePreferences().PreferredKey31Fifths);
            for (int key = -15; key <= 15; key++) {
                Assert.Equal(31, Enumerable.Range(0, 31).Select(s => Edo31.FifthsForStep(s, key)).Distinct().Count());
                for (int step = 0; step < Edo31.MaxStep; step++) {
                    Assert.Equal(step, Edo31.ParseName(Edo31.Name(step, key)));
                    Assert.InRange(Edo31.ColorFamily(step), 0, 4);
                }
            }
            Assert.Equal(5, Enumerable.Range(0, 31).Select(Edo31.ColorFamily).Distinct().Count());
            Assert.Equal("C4", Edo31.Name(155, 2));
            Assert.NotEqual(Edo31.ParseName("C#4"), Edo31.ParseName("Db4"));
        }
        [Fact]
        public void NativeRoundTripPreservesExactStepAndCentsWithoutPreferredKey() {
            var text = SaveText(Fixture(true));
            Assert.StartsWith("format: " + Ustx31.FormatId, text);
            Assert.DoesNotContain("ustx_version:", text);
            Assert.DoesNotContain("key:", text);
            Assert.DoesNotContain("preferred", text);
            Assert.Contains("features:", text);
            var project = Ustx31.Deserialize(text);
            var note = Assert.Single(Assert.Single(project.voiceParts).notes);
            Assert.True(project.Is31Edo);
            Assert.Equal(156, note.tone31);
            Assert.Equal(7, note.tuning);
            Assert.InRange(Math.Abs(note.AdjustedTone - (156 * 12.0 / 31 + .07)), 0, 0.00001);
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
            Assert.Equal(7, loaded.voiceParts[0].notes.First().tuning);
        }
        [Theory]
        [InlineData("format_revision: 1", "format_revision: 2")]
        [InlineData("edo31-v1", "unknown-v1")]
        [InlineData("tone31: 156", "tone31: -1")]
        [InlineData("tone31: 156", "tone31: 341")]
        [InlineData("tone31: 156", "unrecognized: 156")]
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
        public void FormatDetectorUsesNativeContentAndLoaderRestoresMode() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ustx31");
            try {
                File.WriteAllText(path, SaveText(Fixture(true)));
                Assert.Equal(ProjectFormats.Ustx, Formats.DetectProjectFormat(path));
                var loaded = Format.Ustx.Load(path);
                Assert.True(loaded.Is31Edo);
                Assert.Equal(156, loaded.parts.OfType<UVoicePart>().Single().notes.First().tone31);
            } finally { File.Delete(path); }
        }
        [Fact]
        public void MoveUndoAndClonePreserveNativePitch() {
            var project = Fixture(true);
            var part = (UVoicePart)project.parts[0];
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
        public void ConversionLeavesSourceIntactAndExportsWithinHalfCent() {
            var source = Fixture(true);
            var original = SaveText(source);
            var copy = Ustx31.ConvertCopy(source, false);
            Assert.Equal(original, SaveText(source));
            Assert.False(copy.Is31Edo);
            var note = copy.parts.OfType<UVoicePart>().Single().notes.First();
            Assert.Null(note.tone31);
            Assert.InRange(Math.Abs(note.AdjustedTone - source.parts.OfType<UVoicePart>().Single().notes.First().AdjustedTone) * 100, 0, 0.501);
            Assert.DoesNotContain("features", SaveText(copy));
            var native = Ustx31.ConvertCopy(copy, true);
            Assert.Equal(156, native.parts.OfType<UVoicePart>().Single().notes.First().tone31);
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
            Assert.Single(ordinary.tracks);
            Assert.True(native.CloneAsTemplate().Is31Edo);
        }
    }
}
