using System;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Plugins {
    public class VoiceColorDefaultTest {
        [Theory]
        [InlineData(null, 0, "")]
        [InlineData(new string[0], 0, "")]
        [InlineData(new[] { "", "Soft" }, -1, "")]
        [InlineData(new[] { "", "Soft" }, 2, "")]
        [InlineData(new[] { "", "Soft" }, 0, "")]
        [InlineData(new[] { "", "Soft" }, 1, "Soft")]
        public void ResolvesAvailableColorOrUsesUncoloredVoice(string[] colors, int index, string expected) {
            var project = Ustx.Create();
            var track = project.tracks[0];
            track.VoiceColorExp = project.expressions[Ustx.CLR].Clone();
            track.VoiceColorExp.options = colors;
            track.VoiceColorExp.CustomDefaultValue = index;
            var phonemizer = new DefaultPhonemizer();
            phonemizer.SetUp(Array.Empty<Phonemizer.Note[]>(), project, track);
            Assert.Equal(expected, phonemizer.GetParentVoiceColor());
        }

        [Fact]
        public void MissingSingerColorDescriptorUsesUncoloredVoice() {
            var project = Ustx.Create();
            var phonemizer = new DefaultPhonemizer();
            phonemizer.SetUp(Array.Empty<Phonemizer.Note[]>(), project, project.tracks[0]);
            Assert.Equal("", phonemizer.GetParentVoiceColor());
        }
    }
}
