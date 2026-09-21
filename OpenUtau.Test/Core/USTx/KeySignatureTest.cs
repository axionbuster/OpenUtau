using System;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class KeySignatureTest {
        static UProject Project(bool is31) {
            var p = Ustx.Create();
            p.Is31Edo = is31;
            p.keySignatures = new() {
                new() { key = 0, major = true, minor = false },
                new() { position = 960, key = 2, major = false, minor = true },
                new() { position = 1920, key = 5, major = false, minor = false },
            };
            return p;
        }
        static string Save(UProject p) {
            p.BeforeSave();
            try { return Ustx31.Serialize(p); } finally { p.AfterSave(); }
        }
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RoundTripAndExactBoundary(bool is31) {
            var p = Ustx31.Deserialize(Save(Project(is31)));
            Assert.Equal(3, p.keySignatures.Count);
            Assert.Equal(0, p.KeyAt(-1).key);
            Assert.Equal(0, p.KeyAt(959).key);
            Assert.Equal(2, p.KeyAt(960).key);
            Assert.Equal("Minor", p.KeyAt(1919).ModeName);
            Assert.Equal("Chromatic", p.KeyAt(int.MaxValue).ModeName);
        }
        [Fact]
        public void EditDeleteUndoRedoPreservePitchAndReference() {
            var p = Project(true);
            var note = p.CreateGridNote(160, 1000, 480);
            double frequency = p.ToneToFrequency(note.PreciseAdjustedTone);
            var original = Save(p);
            var changes = p.keySignatures.Select(k => k.Clone()).ToList();
            changes.RemoveAt(1);
            var command = new KeySignatureCommand(p, changes);
            command.Execute();
            Assert.Equal(0, p.KeyAt(960).key);
            Assert.Equal(frequency, p.ToneToFrequency(note.PreciseAdjustedTone));
            command.Unexecute();
            Assert.Equal(original, Save(p));
            command.Execute();
            Assert.Equal(2, p.keySignatures.Count);
        }
        [Fact]
        public void ConversionPreservesTimelineAndMapsTonic() {
            var p = Project(false);
            var native = Ustx31.ConvertCopy(p, true);
            Assert.Equal(2, native.KeyAt(960).key); // D is two fifths and pitch class two.
            Assert.Equal(-1, native.KeyAt(1920).key); // F
            var back = Ustx31.ConvertCopy(native, false);
            Assert.Equal(p.keySignatures.Select(k => (k.position, k.key, k.major, k.minor)),
                back.keySignatures.Select(k => (k.position, k.key, k.major, k.minor)));
        }
        [Fact]
        public void Legacy12TetUsesSavedKeyAnd31TetUsesDeterministicDefault() {
            var p = Ustx.Create();
            p.key = 9;
            p.BeforeSave();
            var legacy = Yaml.DefaultSerializer.Serialize(p);
            p.AfterSave();
            Assert.Equal(9, Ustx31.Deserialize(legacy).KeyAt(0).key);
            p.Is31Edo = true;
            var text = Save(p).Replace("format_revision: 3", "format_revision: 2")
                .Replace("- key-signatures-v1\n", "");
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"  key_signatures:\n(?:  - .*\n|    .*\n)*", "");
            Assert.Equal(2, Ustx31.Deserialize(text).KeyAt(0).key);
        }
        [Fact]
        public void InvalidTimelinesFailInsteadOfSilentlyChangingTheirMeaning() {
            var p = Project(true);
            p.keySignatures[1].position = 0;
            Assert.Throws<FileFormatException>(() => Save(p));
            p.keySignatures[1].position = 960;
            p.keySignatures[1].key = 16;
            Assert.Throws<FileFormatException>(() => Save(p));
            p.keySignatures.Clear();
            Assert.Throws<FileFormatException>(() => Save(p));
        }
    }
}
