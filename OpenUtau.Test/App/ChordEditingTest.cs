using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.App {
    [Collection(RenderSingletonCollection.Name)]
    public class ChordEditingTest {
        static UProject Fixture() {
            var project = Core.Format.Ustx.Create();
            project.tracks.Add(new UTrack("Voice") { TrackNo = 0 });
            project.parts.Add(new UVoicePart { trackNo = 0, position = 0, Duration = 7680 });
            project.EnsureChordsTrack();
            return project;
        }

        /// <summary>A headless piano roll with the Chords track open and Ch mode on.</summary>
        sealed class Editor : IDisposable {
            public readonly PianoRollViewModel ViewModel;
            public readonly PianoRoll Control;
            public readonly Window Window;
            public readonly NotesCanvas Canvas;
            readonly UProject previousProject;
            readonly Action<UCommand>? previousSink;

            public NotesViewModel Notes => ViewModel.NotesViewModel;

            public Editor(UProject project) {
                ThreadGuard.SetUiThread(Thread.CurrentThread);
                previousProject = DocManager.Inst.TakeProjectForTest(project);
                previousSink = DocManager.Inst.CommandSink;
                DocManager.Inst.SearchAllLegacyPlugins();
                bool live = false;
                PianoRollViewModel? model = null;
                DocManager.Inst.CommandSink = cmd => {
                    if (!live || model == null) {
                        return;
                    }
                    if (cmd is not UNotification) {
                        cmd.Execute();
                    }
                    model.NotesViewModel.OnNext(cmd, false);
                    model.ChordHelpers.OnCommand(cmd);
                };
                ViewModel = model = new PianoRollViewModel();
                Control = new PianoRoll(ViewModel);
                Window = new Window { Width = 1200, Height = 800, Content = Control };
                Window.Show();
                ViewModel.NotesViewModel.OnNext(
                    new LoadPartNotification(project.ChordsPart, project, 0), false);
                ViewModel.NotesViewModel.TrackHeight = 22;
                ViewModel.NotesViewModel.TickWidth = 0.1;
                ViewModel.NotesViewModel.TrackOffset = ViewModel.NotesViewModel.TrackCount - 1 - 72;
                ViewModel.ChordHelperMode = true;
                Canvas = Control.GetVisualDescendants().OfType<NotesCanvas>().Single();
                Dispatcher.UIThread.RunJobs();
                live = true;
            }

            /// <summary>The middle of one grid cell, in the notes canvas's own space.</summary>
            public Point Local(double tick, int step) {
                var local = Notes.TickToneToPoint(tick, Notes.GridToTone(step));
                return new Point(local.X + 4, local.Y + Notes.TrackHeight / 2);
            }

            public Point At(double tick, int step) =>
                Canvas.TranslatePoint(Local(tick, step), Window)!.Value;

            public int SnappedTick(double tick, int step) {
                Notes.PointToLineTick(Local(tick, step), out int left, out _);
                return left;
            }

            public void Click(double tick, int step, MouseButton button = MouseButton.Left) {
                var point = At(tick, step);
                Window.MouseDown(point, button);
                Window.MouseUp(point, button);
                Dispatcher.UIThread.RunJobs();
            }

            public void Drag(double fromTick, int fromStep, double toTick, int toStep) {
                Window.MouseDown(At(fromTick, fromStep), MouseButton.Left);
                Window.MouseMove(At(toTick, toStep), RawInputModifiers.LeftMouseButton);
                Window.MouseUp(At(toTick, toStep), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }

            public void Dispose() {
                DocManager.Inst.CommandSink = previousSink;
                Window.Close();
                DocManager.Inst.TakeProjectForTest(previousProject);
                ThreadGuard.SetUiThread(null);
            }
        }

        [AvaloniaFact]
        public void ClickingEmptyGridOpensARegionAtThatTickAndPutsTheChordInIt() {
            var project = Fixture();
            using var editor = new Editor(project);
            var chordPart = project.ChordsPart;

            editor.Click(0, 60);
            var region = Assert.Single(chordPart.chordRegions);
            var helper = Assert.Single(region.chordHelpers);
            Assert.Empty(chordPart.chordHelpers);
            Assert.Equal(0, region.position);
            Assert.Equal(0, helper.position);
            Assert.Equal(60, helper.rootTone);

            // A chord placed past that region gets a region of its own rather than
            // being folded back into the selected region's loop.
            int expected = editor.SnappedTick(3840, 64);
            editor.Click(3840, 64);
            Assert.Equal(2, chordPart.chordRegions.Count);
            var second = chordPart.chordRegions[1];
            var secondHelper = Assert.Single(second.chordHelpers);
            Assert.Equal(expected, second.position);
            Assert.Equal(0, secondHelper.position);
            Assert.Equal(64, secondHelper.rootTone);
        }

        [AvaloniaFact]
        public void DraggingAChordMovesItInTimeAndTransposesItWithoutChangingQuality() {
            var project = Fixture();
            using var editor = new Editor(project);
            var chordPart = project.ChordsPart;

            editor.Click(0, 60);
            var helper = chordPart.chordRegions.Single().chordHelpers.Single();
            var spelling = helper.tones.Select(tone => (tone.degree, tone.alteration)).ToArray();
            Assert.Equal("Major", ChordHelperTheory.QualityName(helper.tones, false));

            int delta = editor.SnappedTick(1920, 67) - editor.SnappedTick(0, 60);
            editor.Drag(0, 60, 1920, 67);

            Assert.Equal(delta, helper.position);
            Assert.Equal(67, helper.rootTone);
            Assert.Equal(7, helper.root);
            Assert.Equal(spelling, helper.tones.Select(tone => (tone.degree, tone.alteration)).ToArray());
            Assert.Equal("Major", ChordHelperTheory.QualityName(helper.tones, false));
        }

        [AvaloniaFact]
        public void ChordsNeverOverlapAndRightClickRemovesOne() {
            var project = Fixture();
            using var editor = new Editor(project);
            var chordPart = project.ChordsPart;
            var region = new UChordRegion { position = 0, sourceDuration = 7680, duration = 7680 };
            var first = new UChordHelper {
                position = 0, duration = 1920, root = 0, rootTone = 60,
                tones = ChordHelperTheory.CreatePreset("Major"),
            };
            var swallowed = new UChordHelper {
                position = 1200, duration = 120, root = 0, rootTone = 72,
                tones = ChordHelperTheory.CreatePreset("Major"),
            };
            region.chordHelpers.Add(first);
            region.chordHelpers.Add(swallowed);
            chordPart.chordRegions.Add(region);
            Dispatcher.UIThread.RunJobs();

            // Dropping a chord over the first one cuts it back to where the new one
            // starts, and takes the short one it covers completely with it.
            int start = editor.SnappedTick(960, 61);
            editor.Click(960, 61);
            var added = Assert.Single(region.chordHelpers, helper => helper.rootTone == 61);
            Assert.Equal(start, added.position);
            Assert.Equal(start, first.End);
            Assert.DoesNotContain(region.chordHelpers, helper => ReferenceEquals(helper, swallowed));
            Assert.Equal(2, region.chordHelpers.Count);

            editor.Click(added.position, 61, MouseButton.Right);
            Assert.DoesNotContain(region.chordHelpers, helper => helper.rootTone == 61);
            Assert.Single(region.chordHelpers);

            // Emptying a region takes the region with it.
            editor.Click(0, 60, MouseButton.Right);
            Assert.Empty(region.chordHelpers);
            Assert.Empty(chordPart.chordRegions);
        }
    }
}
