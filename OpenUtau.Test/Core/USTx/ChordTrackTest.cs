using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.SignalChain;
using Xunit;

namespace OpenUtau.Core.Ustx {
    public class ChordTrackTest {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NewProjectRoundTripKeepsOneConfiguredChordTrack(bool native) {
            var project = Format.Ustx.Create();
            project.Is31Edo = native;
            project.ChordsTrack.TrackColor = "Pink";
            project.ChordsTrack.Mute = true;
            project.ChordsTrack.Volume = -4.5;
            project.ChordsTrack.Pan = 23;
            project.BeforeSave();
            string text;
            try { text = Ustx31.Serialize(project); }
            finally { project.AfterSave(); }

            var loaded = Ustx31.Deserialize(text);
            loaded.AfterLoad();
            Assert.Same(loaded.tracks[0], Assert.Single(loaded.tracks.Where(track => track.IsChordsTrack)));
            Assert.Single(loaded.parts.OfType<UVoicePart>().Where(part => part.IsChordPart));
            Assert.Equal("Pink", loaded.ChordsTrack.TrackColor);
            Assert.True(loaded.ChordsTrack.Mute);
            Assert.Equal(-4.5, loaded.ChordsTrack.Volume);
            Assert.Equal(23, loaded.ChordsTrack.Pan);
        }

        [Fact]
        public void LegacyPartsMigrateAtAbsoluteTicksWithoutDeduplicatingOverlap() {
            var project = Format.Ustx.Create();
            project.tracks.RemoveAll(track => track.IsChordsTrack);
            project.parts.RemoveAll(part => part is UVoicePart voice && voice.IsChordPart);
            project.tracks.Clear();
            project.tracks.Add(new UTrack("A") { TrackNo = 0 });
            project.tracks.Add(new UTrack("B") { TrackNo = 1 });
            var first = new UVoicePart { trackNo = 0, position = 120, duration = 960 };
            first.chordHelpers.Add(new UChordHelper { position = 20, duration = 100 });
            first.chordHelpers.Add(new UChordHelper { position = 20, duration = 100 });
            var second = new UVoicePart { trackNo = 1, position = 400, duration = 960 };
            second.chordHelpers.Add(new UChordHelper { position = 10, duration = 200 });
            project.parts.Add(first);
            project.parts.Add(second);

            project.EnsureChordsTrack();

            Assert.Equal(new[] { 140, 140, 410 }, project.ChordsPart.chordHelpers.Select(helper => helper.position));
            Assert.Empty(first.chordHelpers);
            Assert.Empty(second.chordHelpers);
            Assert.Equal(3, project.ChordsPart.chordHelpers.Count);
            Assert.Equal(1, first.trackNo);
            Assert.Equal(2, second.trackNo);
        }

        [Fact]
        public void ChordRoleCannotBeRemovedDuplicatedOrCrossed() {
            var project = Format.Ustx.Create();
            var chords = project.ChordsTrack;
            new RemoveTrackCommand(project, chords).Execute();
            Assert.Same(chords, project.tracks[0]);
            new AddTrackCommand(project, UTrack.CreateChordsTrack()).Execute();
            Assert.Single(project.tracks.Where(track => track.IsChordsTrack));
            var voice = project.tracks[1];
            new MoveTrackCommand(project, voice, true).Execute();
            Assert.Same(chords, project.tracks[0]);
        }

        [Fact]
        public void NotesCannotBeAddedToChordContent() {
            var project = Format.Ustx.Create();
            new AddNoteCommand(project.ChordsPart, project.CreateNote()).Execute();
            Assert.Empty(project.ChordsPart.notes);
        }

        [Fact]
        public void ChordMixStateIsIndependentFromVocalTrack() {
            var project = Format.Ustx.Create();
            var voice = project.tracks.Single(track => !track.IsChordsTrack);
            project.ChordsPart.chordHelpers.Add(new UChordHelper {
                duration = 480,
                rootTone = 60,
                tones = ChordHelperTheory.CreatePreset("Major"),
            });
            project.ChordsTrack.Volume = -6;
            project.ChordsTrack.Pan = 100;

            voice.Mute = true;
            var audible = Assert.Single(ChordHelperPlaybackSnapshot.Create(project).Events);
            Assert.Equal(PlaybackManager.DecibelToVolume(-6), audible.TrackScale);
            Assert.Equal(0, audible.PanLeft, 6);
            Assert.Equal(1, audible.PanRight, 6);

            project.ChordsTrack.Mute = true;
            Assert.Empty(ChordHelperPlaybackSnapshot.Create(project).Events);
            project.ChordsTrack.Mute = false;
            voice.Mute = false;
            voice.Solo = true;
            Assert.Empty(ChordHelperPlaybackSnapshot.Create(project).Events);
            voice.Solo = false;
            project.ChordsTrack.Solo = true;
            Assert.Single(ChordHelperPlaybackSnapshot.Create(project).Events);
        }

        [Fact]
        public void MalformedChordRoleRecoversNotesAndCurvesOnVoiceTrack() {
            var project = Format.Ustx.Create();
            var note = project.CreateNote();
            project.ChordsPart.notes.Add(note);
            project.ChordsPart.curves.Add(new UCurve());
            project.EnsureChordsTrack();
            Assert.Empty(project.ChordsPart.notes);
            Assert.Empty(project.ChordsPart.curves);
            var recovered = Assert.Single(project.parts.OfType<UVoicePart>(), part => !part.IsChordPart);
            Assert.Contains(note, recovered.notes);
            Assert.Single(recovered.curves);
            Assert.False(project.tracks[recovered.trackNo].IsChordsTrack);
        }
    }
}
